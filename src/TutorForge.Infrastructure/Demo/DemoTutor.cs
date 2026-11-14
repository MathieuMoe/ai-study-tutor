using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Validation;

namespace TutorForge.Infrastructure.Demo;

/// <summary>
/// Offline tutor, always available. It follows the pedagogical script of each mode with a small bank of
/// multiple-choice questions per subject and language (demo-questions.json) and checks the chosen letter.
/// Each conversation gets its own random order of questions, carried in a hidden [[SEED:n]] tag so the
/// server can stay stateless. Answers are streamed word by word like the real tutor.
/// </summary>
public sealed partial class DemoTutor(TimeProvider timeProvider) : ITutor
{
    private const int AssessQuestions = 5;
    private static readonly TimeSpan WordDelay = TimeSpan.FromMilliseconds(18);
    private static readonly Lazy<Dictionary<Subject, Dictionary<TutorLanguage, DemoQuestion[]>>> Bank = new(LoadBank);

    public DemoTutor() : this(TimeProvider.System) { }

    public string EngineName => "Demo (offline)";

    public async IAsyncEnumerable<string> StreamReplyAsync(TutorRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var errors = TutorRequestValidator.Check(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)), nameof(request));
        }

        var reply = BuildReply(request);
        foreach (var word in reply.Split(' '))
        {
            await Task.Delay(WordDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            yield return word + " ";
        }
    }

    internal static string BuildReply(TutorRequest request, int? newSeed = null)
    {
        var turn = request.Messages.Count(m => m.Role == ChatRole.User);
        var s = Script(request.Language);
        var questions = Questions(request.Subject, request.Language);
        var subjectName = s.Subjects.GetValueOrDefault(request.Subject, request.Subject.ToString());

        // Random order per conversation: reuse the seed of the conversation, or create one on the first answer.
        var existingSeed = FindSeed(request.Messages);
        var seed = existingSeed ?? newSeed ?? Random.Shared.Next(1, 1_000_000);
        var order = Shuffle(questions.Length, seed);
        DemoQuestion Question(int index) => questions[order[index % order.Length]];
        string Q(int index) => Question(index).Format();
        var seedTag = existingSeed is null ? $" [[SEED:{seed}]]" : string.Empty;

        // Feedback on the previous multiple-choice question: the student's letter is compared with the right one.
        var lastQuestion = request.Mode == TutorMode.Assess && turn == AssessQuestions + 1;
        var feedback = turn >= 2 ? Feedback(Question(turn - 2), request.Messages[^1].Content, s, lastQuestion) : s.Right;

        var body = request.Mode switch
        {
            TutorMode.Assess when turn == 1 =>
                $"{string.Format(CultureInfo.InvariantCulture, s.AssessIntro, subjectName, AssessQuestions)}\n\n**{Label(s.QuestionLabel, 1)}** {Q(0)}",
            TutorMode.Assess when turn <= AssessQuestions =>
                $"{feedback}\n\n**{Label(s.QuestionLabel, turn)}** {Q(turn - 1)}",
            TutorMode.Assess when turn == AssessQuestions + 1 =>
                $"{feedback}\n\n" + string.Format(CultureInfo.InvariantCulture, s.Report, subjectName, 4 + (seed % 4)) + $" [[LEVEL:{4 + (seed % 4)}]]",
            TutorMode.Assess =>
                $"{(turn == AssessQuestions + 2 ? s.Growing : feedback)}\n\n**{s.Challenge}** {Q(turn - 1)} [[LEVEL:{Math.Min(4 + (seed % 4) + turn - AssessQuestions - 1, 10)}]]",

            TutorMode.Practice when turn == 1 =>
                $"{string.Format(CultureInfo.InvariantCulture, s.PracticeIntro, subjectName)}\n\n**{Label(s.ExerciseLabel, 1)}** {Q(0)}",
            TutorMode.Practice =>
                $"{feedback}\n\n**{Label(s.ExerciseLabel, turn)}** {Q(turn - 1)} [[LEVEL:{Math.Min(3 + turn, 9)}]]",

            TutorMode.Explain => string.Format(CultureInfo.InvariantCulture, s.Explain, Topic(request.Messages[^1].Content), subjectName),
            TutorMode.Check => s.Check,
            _ => turn switch
            {
                1 => string.Format(CultureInfo.InvariantCulture, s.GuideFirst, Topic(request.Messages[^1].Content)),
                2 => s.GuideSecond,
                _ => s.GuideNext,
            },
        };

        return body + "\n\n" + s.DemoNote + seedTag;
    }

    private static string Label(string format, int number) =>
        string.Format(CultureInfo.InvariantCulture, format, number, AssessQuestions);

    /// <summary>"B" or "B) text" chosen by the student versus the right answer.</summary>
    internal static string Feedback(DemoQuestion question, string answer, Texts s, bool lastQuestion = false)
    {
        var match = AnswerLetter().Match(answer);
        if (match.Success && char.ToUpperInvariant(match.Groups[1].Value[0]) - 'A' == question.Answer)
        {
            return lastQuestion ? s.Right : $"{s.Right} {s.Harder}";
        }

        var correction = $"{s.Wrong} {string.Format(CultureInfo.InvariantCulture, s.CorrectWas, DemoQuestion.Letter(question.Answer), question.Options[question.Answer])}";
        return lastQuestion ? correction : $"{correction} {s.Another}";
    }

    private static DemoQuestion[] Questions(Subject subject, TutorLanguage language) =>
        Bank.Value.TryGetValue(subject, out var byLanguage) && byLanguage.TryGetValue(language, out var list) && list.Length > 0
            ? list
            : Bank.Value[Subject.Other][language];

    /// <summary>Deterministic Fisher-Yates shuffle: the same seed always gives the same order.</summary>
    internal static int[] Shuffle(int count, int seed)
    {
        var order = Enumerable.Range(0, count).ToArray();
#pragma warning disable CA5394 // not security-sensitive: only shuffles demo questions
        var random = new Random(seed);
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
#pragma warning restore CA5394
        return order;
    }

    private static int? FindSeed(IEnumerable<ChatMessage> messages)
    {
        foreach (var m in messages.Where(m => m.Role == ChatRole.Assistant))
        {
            var match = SeedTag().Match(m.Content);
            if (match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var seed))
            {
                return seed;
            }
        }

        return null;
    }

    private static string Topic(string message)
    {
        var words = message.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var topic = string.Join(' ', words.Take(8));
        return words.Length > 8 ? topic + "…" : topic;
    }

    private static Dictionary<Subject, Dictionary<TutorLanguage, DemoQuestion[]>> LoadBank()
    {
        using var stream = typeof(DemoTutor).Assembly.GetManifestResourceStream("TutorForge.Infrastructure.Demo.demo-questions.json")
            ?? throw new InvalidOperationException("Embedded demo question bank not found.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, DemoQuestion[]>>>(stream, JsonOptions)!;
        return raw.ToDictionary(
            s => Enum.Parse<Subject>(s.Key),
            s => s.Value.ToDictionary(l => Enum.Parse<TutorLanguage>(l.Key), l => l.Value));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"\[\[SEED:(\d+)\]\]")]
    private static partial Regex SeedTag();

    /// <summary>The letter at the start of the answer: "b", "B)", "B. text", "**B**"...</summary>
    [GeneratedRegex(@"^\W*([A-Da-d])(?:\W|$)")]
    private static partial Regex AnswerLetter();

    /// <summary>A multiple-choice question of the demo bank. Shown with one "A) option" per line, like the AI tutors.</summary>
    internal sealed record DemoQuestion(string Q, string[] Options, int Answer)
    {
        public static string Letter(int index) => ((char)('A' + index)).ToString();

        public string Format() =>
            Q + "\n\n" + string.Join("\n", Options.Select((o, i) => $"{Letter(i)}) {o}"));
    }

    internal sealed record Texts(
        string AssessIntro, string Right, string Harder, string Wrong, string CorrectWas, string Another, string QuestionLabel, string Report, string Growing, string Challenge,
        string PracticeIntro, string ExerciseLabel, string GuideFirst, string GuideSecond, string GuideNext,
        string Explain, string Check, string DemoNote, Dictionary<Subject, string> Subjects);

    internal static Texts BuildTexts(TutorLanguage language) => Script(language);

    private static Texts Script(TutorLanguage language) => language switch
    {
        TutorLanguage.Spanish => new(
            "¡Vamos a ver tu nivel en **{0}**! 🎯 Te haré **{1} preguntas cortas**, adaptadas a cómo respondes. Si no sabes una, no pasa nada: dímelo.",
            "✅ ¡Bien!", "Subimos un poco la dificultad.",
            "❌ No exactamente, pero buen intento.",
            "La respuesta correcta era **{0}) {1}**.", "Vamos con otra.",
            "Pregunta {0}/{1}:",
            "📊 **Tu informe de nivel en {0}**\n\n**Nivel estimado: {1}/10**: tienes una buena base y algunos puntos que reforzar.\n\n**Puntos fuertes:** las preguntas de conceptos básicos.\n**A trabajar:** las preguntas que piden explicar o razonar.\n\n**Siguiente paso:** ¿seguimos entrenando? Te propondré retos a tu nivel y subiremos la dificultad a medida que aciertes.",
            "¡Muy bien, vas subiendo! 📈",
            "Siguiente reto:",
            "¡Vamos a practicar **{0}**! 🎯 Elige una respuesta o escríbela. Equivocarse es parte de aprender.",
            "Ejercicio {0}:",
            "¡Buena pregunta! Vamos a resolverlo juntos, paso a paso, sobre «{0}».\n\n**Antes de nada:** ¿qué sabes ya sobre esto o qué has intentado? Escríbelo con tus palabras, aunque no estés seguro.",
            "¡Muy bien, vas por buen camino! 💡 **Pista:** divide el problema en partes más pequeñas. ¿Cuál sería el primer paso?",
            "Estás muy cerca. Revisa el último paso y compruébalo con un ejemplo concreto. ¡Tú puedes!",
            "Te explico «{0}» de forma sencilla.\n\n**1. Idea principal:** la definición en una frase.\n\n**2. Ejemplo cotidiano:** un caso real de {1} donde se ve claramente.\n\n**3. Comprueba:** ¿podrías explicarme con tus palabras qué has entendido?",
            "Gracias por compartir tu respuesta. ✅ El planteamiento va bien, pero revisa el **segundo paso**: parece que hay un pequeño error.\n\n**Pista:** vuelve a hacerlo despacio y compara. ¿Qué obtienes?",
            "_Modo demo sin conexión: las preguntas salen de un pequeño banco de ejemplo. Configura una IA (Gemini es gratis) para hablar con un tutor real._",
            new() { [Subject.Mathematics] = "Matemáticas", [Subject.PhysicsChemistry] = "Física y química", [Subject.Biology] = "Biología", [Subject.HistoryGeography] = "Geografía e historia", [Subject.LanguageLiterature] = "Lengua y literatura", [Subject.English] = "Inglés", [Subject.Programming] = "Programación", [Subject.Other] = "estudio" }),
        TutorLanguage.French => new(
            "Voyons ton niveau en **{0}** ! 🎯 Je vais te poser **{1} questions courtes**, adaptées à tes réponses. Si tu ne sais pas, ce n'est pas grave : dis-le-moi.",
            "✅ Bien !", "On augmente un peu la difficulté.",
            "❌ Pas tout à fait, mais bel essai.",
            "La bonne réponse était **{0}) {1}**.", "On passe à une autre.",
            "Question {0}/{1} :",
            "📊 **Ton bilan de niveau en {0}**\n\n**Niveau estimé : {1}/10** : tu as une bonne base et quelques points à renforcer.\n\n**Points forts :** les questions de notions de base.\n**À travailler :** les questions où il faut expliquer ou raisonner.\n\n**Prochaine étape :** on continue l'entraînement ? Je te proposerai des défis à ton niveau et on augmentera la difficulté au fur et à mesure.",
            "Très bien, tu progresses ! 📈",
            "Défi suivant :",
            "On s'entraîne en **{0}** ! 🎯 Choisis une réponse ou écris-la. Se tromper fait partie de l'apprentissage.",
            "Exercice {0} :",
            "Bonne question ! On va le résoudre ensemble, étape par étape, à propos de « {0} ».\n\n**D'abord :** que sais-tu déjà là-dessus ou qu'as-tu essayé ? Écris-le avec tes mots, même si tu n'es pas sûr.",
            "Très bien, tu es sur la bonne voie ! 💡 **Indice :** découpe le problème en petites étapes. Quelle serait la première ?",
            "Tu y es presque. Vérifie la dernière étape et teste-la avec un exemple concret. Tu peux y arriver !",
            "Je t'explique « {0} » simplement.\n\n**1. L'idée principale :** la définition en une phrase.\n\n**2. Exemple du quotidien :** un cas réel de {1} où on le voit clairement.\n\n**3. Vérifions :** peux-tu m'expliquer avec tes mots ce que tu as compris ?",
            "Merci d'avoir partagé ta réponse. ✅ Le raisonnement est bon, mais regarde la **deuxième étape** : il semble y avoir une petite erreur.\n\n**Indice :** refais-la lentement et compare. Qu'obtiens-tu ?",
            "_Mode démo hors ligne : les questions viennent d'une petite banque d'exemples. Configure une IA (Gemini est gratuit) pour parler avec un vrai tuteur._",
            new() { [Subject.Mathematics] = "Mathématiques", [Subject.PhysicsChemistry] = "Physique-chimie", [Subject.Biology] = "Biologie", [Subject.HistoryGeography] = "Histoire-géographie", [Subject.LanguageLiterature] = "Français", [Subject.English] = "Anglais", [Subject.Programming] = "Programmation", [Subject.Other] = "méthodologie" }),
        TutorLanguage.Catalan => new(
            "Anem a veure el teu nivell en **{0}**! 🎯 Et faré **{1} preguntes curtes**, adaptades a com respons. Si no en saps una, no passa res: digues-m'ho.",
            "✅ Bé!", "Pugem una mica la dificultat.",
            "❌ No exactament, però bon intent.",
            "La resposta correcta era **{0}) {1}**.", "Anem amb una altra.",
            "Pregunta {0}/{1}:",
            "📊 **El teu informe de nivell en {0}**\n\n**Nivell estimat: {1}/10**: tens una bona base i alguns punts per reforçar.\n\n**Punts forts:** les preguntes de conceptes bàsics.\n**A treballar:** les preguntes que demanen explicar o raonar.\n\n**Següent pas:** seguim entrenant? Et proposaré reptes al teu nivell i pujarem la dificultat a mesura que encertis.",
            "Molt bé, vas pujant! 📈",
            "Següent repte:",
            "Practiquem **{0}**! 🎯 Tria una resposta o escriu-la. Equivocar-se és part d'aprendre.",
            "Exercici {0}:",
            "Bona pregunta! Ho resoldrem junts, pas a pas, sobre «{0}».\n\n**Primer de tot:** què en saps o què has provat? Escriu-ho amb les teves paraules, encara que no n'estiguis segur.",
            "Molt bé, vas pel bon camí! 💡 **Pista:** divideix el problema en parts més petites. Quin seria el primer pas?",
            "Hi ets a prop. Revisa l'últim pas i comprova'l amb un exemple concret. Tu pots!",
            "T'explico «{0}» de manera senzilla.\n\n**1. Idea principal:** la definició en una frase.\n\n**2. Exemple quotidià:** un cas real de {1} on es veu clarament.\n\n**3. Comprovem-ho:** em podries explicar amb les teves paraules què has entès?",
            "Gràcies per compartir la teva resposta. ✅ El plantejament va bé, però revisa el **segon pas**: sembla que hi ha un petit error.\n\n**Pista:** torna-ho a fer a poc a poc i compara. Què obtens?",
            "_Mode demo sense connexió: les preguntes surten d'un petit banc d'exemple. Configura una IA (Gemini és gratuït) per parlar amb un tutor real._",
            new() { [Subject.Mathematics] = "Matemàtiques", [Subject.PhysicsChemistry] = "Física i química", [Subject.Biology] = "Biologia", [Subject.HistoryGeography] = "Geografia i història", [Subject.LanguageLiterature] = "Llengua i literatura", [Subject.English] = "Anglès", [Subject.Programming] = "Programació", [Subject.Other] = "estudi" }),
        _ => new(
            "Let's find your level in **{0}**! 🎯 I'll ask you **{1} short questions**, adapted to your answers. If you don't know one, that's fine: just tell me.",
            "✅ Good!", "Let's make it a bit harder.",
            "❌ Not quite, but good try.",
            "The right answer was **{0}) {1}**.", "Let's go with another one.",
            "Question {0}/{1}:",
            "📊 **Your level report in {0}**\n\n**Estimated level: {1}/10**: you have a good base and a few things to strengthen.\n\n**Strengths:** the questions on basic concepts.\n**To work on:** the questions that ask you to explain or reason.\n\n**Next step:** shall we keep training? I'll give you challenges at your level and make them harder as you succeed.",
            "Great, you're improving! 📈",
            "Next challenge:",
            "Let's practise **{0}**! 🎯 Pick an answer or type it. Mistakes are part of learning.",
            "Exercise {0}:",
            "Great question! Let's solve it together, step by step, about \"{0}\".\n\n**First:** what do you already know about it or what have you tried? Write it in your own words, even if you're not sure.",
            "Well done, you're on the right track! 💡 **Hint:** split the problem into smaller steps. What would the first one be?",
            "You're very close. Check the last step and test it with a concrete example. You can do it!",
            "Let me explain \"{0}\" simply.\n\n**1. Main idea:** the definition in one sentence.\n\n**2. Everyday example:** a real case in {1} where you can clearly see it.\n\n**3. Let's check:** can you tell me in your own words what you understood?",
            "Thanks for sharing your answer. ✅ Your approach is right, but look at the **second step**: there seems to be a small mistake.\n\n**Hint:** redo it slowly and compare. What do you get?",
            "_Offline demo mode: questions come from a small sample bank. Set up an AI (Gemini is free) to talk to a real tutor._",
            new() { [Subject.Mathematics] = "Mathematics", [Subject.PhysicsChemistry] = "Physics & chemistry", [Subject.Biology] = "Biology", [Subject.HistoryGeography] = "History & geography", [Subject.LanguageLiterature] = "Language & literature", [Subject.English] = "English", [Subject.Programming] = "Programming", [Subject.Other] = "study skills" }),
    };
}
