using System.Globalization;
using System.Runtime.CompilerServices;
using TutorForge.Core.Abstractions;
using TutorForge.Core.Models;
using TutorForge.Core.Validation;

namespace TutorForge.Infrastructure.Demo;

/// <summary>
/// Offline tutor used when no API key is configured. It does not understand the question:
/// it follows the pedagogical script of each mode (in 4 languages) so the app can be tried end to end.
/// Answers are streamed word by word like the real one.
/// </summary>
public sealed class DemoTutor(TimeProvider timeProvider) : ITutor
{
    private static readonly TimeSpan WordDelay = TimeSpan.FromMilliseconds(18);

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

    internal static string BuildReply(TutorRequest request)
    {
        var turn = request.Messages.Count(m => m.Role == ChatRole.User);
        var topic = Topic(request.Messages[^1].Content);
        var s = Script(request.Language);

        var body = request.Mode switch
        {
            TutorMode.Explain => string.Format(CultureInfo.InvariantCulture, s.Explain, topic),
            TutorMode.Check => s.Check,
            TutorMode.Practice => turn == 1 ? s.PracticeFirst : s.PracticeNext,
            _ => turn switch
            {
                1 => string.Format(CultureInfo.InvariantCulture, s.GuideFirst, topic),
                2 => s.GuideSecond,
                _ => s.GuideNext,
            },
        };

        return body + "\n\n" + s.DemoNote;
    }

    private static string Topic(string message)
    {
        var words = message.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var topic = string.Join(' ', words.Take(8));
        return words.Length > 8 ? topic + "…" : topic;
    }

    private sealed record Texts(
        string GuideFirst, string GuideSecond, string GuideNext,
        string Explain, string Check, string PracticeFirst, string PracticeNext, string DemoNote);

    private static Texts Script(TutorLanguage language) => language switch
    {
        TutorLanguage.Spanish => new(
            "¡Buena pregunta! Vamos a resolverlo juntos, paso a paso, sobre «{0}».\n\n**Antes de nada:** ¿qué datos te da el enunciado y qué te piden encontrar? Escríbelo con tus palabras, aunque no estés seguro.",
            "¡Muy bien, vas por buen camino! 💡 **Pista:** intenta dividir el problema en partes más pequeñas. Por ejemplo, si tienes $2x + 3 = 11$, ¿qué harías primero para dejar la $x$ sola?",
            "Estás muy cerca. Revisa el último paso: ¿has hecho la misma operación en los dos lados? Cuando lo tengas, compruébalo sustituyendo el resultado en el enunciado. ¡Tú puedes!",
            "Te explico «{0}» de forma sencilla.\n\n**1. Idea principal:** es un concepto que se entiende mejor con un ejemplo.\n\n**2. Ejemplo cotidiano:** imagina que repartes 12 caramelos entre 3 amigos: $12 \\div 3 = 4$.\n\n**3. Comprueba:** ¿podrías explicarme con tus palabras qué has entendido?",
            "Gracias por compartir tu respuesta. ✅ El planteamiento es correcto, pero revisa el **segundo paso**: parece que hay un pequeño error de cálculo.\n\n**Pista:** vuelve a hacer esa operación despacio y compara. ¿Qué obtienes?",
            "¡Vamos a practicar! 🎯\n\n**Ejercicio 1:** resuelve $3x - 5 = 10$.\n\nEscribe tu solución y los pasos que has seguido. No pasa nada si te equivocas: así aprendemos.",
            "¡Buen intento! Lo que has hecho bien: has aislado la incógnita. A mejorar: revisa los signos.\n\n**Ejercicio 2** (un poco más difícil): resuelve $2(x + 4) = 18$.",
            "_Modo demo sin conexión: las respuestas son de ejemplo. Añade una clave de API de Anthropic para hablar con un tutor real._"),
        TutorLanguage.French => new(
            "Bonne question ! On va le résoudre ensemble, étape par étape, à propos de « {0} ».\n\n**D'abord :** quelles données l'énoncé te donne-t-il et que cherche-t-on ? Écris-le avec tes mots, même si tu n'es pas sûr.",
            "Très bien, tu es sur la bonne voie ! 💡 **Indice :** découpe le problème en petites étapes. Par exemple, avec $2x + 3 = 11$, que ferais-tu en premier pour isoler $x$ ?",
            "Tu y es presque. Vérifie la dernière étape : as-tu fait la même opération des deux côtés ? Ensuite, vérifie en remplaçant ton résultat dans l'énoncé. Tu peux y arriver !",
            "Je t'explique « {0} » simplement.\n\n**1. L'idée principale :** c'est plus clair avec un exemple.\n\n**2. Exemple du quotidien :** tu partages 12 bonbons entre 3 amis : $12 \\div 3 = 4$.\n\n**3. Vérifions :** peux-tu m'expliquer avec tes mots ce que tu as compris ?",
            "Merci d'avoir partagé ta réponse. ✅ Le raisonnement est bon, mais regarde la **deuxième étape** : il semble y avoir une petite erreur de calcul.\n\n**Indice :** refais cette opération lentement et compare. Qu'obtiens-tu ?",
            "On s'entraîne ! 🎯\n\n**Exercice 1 :** résous $3x - 5 = 10$.\n\nÉcris ta solution et les étapes suivies. Ce n'est pas grave de se tromper : c'est comme ça qu'on apprend.",
            "Bel effort ! Ce qui est réussi : tu as isolé l'inconnue. À améliorer : attention aux signes.\n\n**Exercice 2** (un peu plus difficile) : résous $2(x + 4) = 18$.",
            "_Mode démo hors ligne : les réponses sont des exemples. Ajoute une clé API Anthropic pour parler avec un vrai tuteur._"),
        TutorLanguage.Catalan => new(
            "Bona pregunta! Ho resoldrem junts, pas a pas, sobre «{0}».\n\n**Primer de tot:** quines dades et dona l'enunciat i què et demanen? Escriu-ho amb les teves paraules, encara que no n'estiguis segur.",
            "Molt bé, vas pel bon camí! 💡 **Pista:** divideix el problema en parts més petites. Per exemple, si tens $2x + 3 = 11$, què faries primer per deixar la $x$ sola?",
            "Hi ets a prop. Revisa l'últim pas: has fet la mateixa operació als dos costats? Quan ho tinguis, comprova-ho substituint el resultat a l'enunciat. Tu pots!",
            "T'explico «{0}» de manera senzilla.\n\n**1. Idea principal:** s'entén millor amb un exemple.\n\n**2. Exemple quotidià:** reparteixes 12 caramels entre 3 amics: $12 \\div 3 = 4$.\n\n**3. Comprovem-ho:** em podries explicar amb les teves paraules què has entès?",
            "Gràcies per compartir la teva resposta. ✅ El plantejament és correcte, però revisa el **segon pas**: sembla que hi ha un petit error de càlcul.\n\n**Pista:** torna a fer aquesta operació a poc a poc i compara. Què obtens?",
            "Practiquem! 🎯\n\n**Exercici 1:** resol $3x - 5 = 10$.\n\nEscriu la teva solució i els passos que has seguit. No passa res si t'equivoques: així aprenem.",
            "Bon intent! El que has fet bé: has aïllat la incògnita. A millorar: compte amb els signes.\n\n**Exercici 2** (una mica més difícil): resol $2(x + 4) = 18$.",
            "_Mode demo sense connexió: les respostes són d'exemple. Afegeix una clau d'API d'Anthropic per parlar amb un tutor real._"),
        _ => new(
            "Great question! Let's solve it together, step by step, about \"{0}\".\n\n**First:** what information does the exercise give you, and what are you asked to find? Write it in your own words, even if you're not sure.",
            "Well done, you're on the right track! 💡 **Hint:** split the problem into smaller steps. For example, with $2x + 3 = 11$, what would you do first to get $x$ on its own?",
            "You're very close. Check the last step: did you do the same operation on both sides? Then verify by putting your result back into the exercise. You can do it!",
            "Let me explain \"{0}\" simply.\n\n**1. Main idea:** it's easier with an example.\n\n**2. Everyday example:** you share 12 sweets between 3 friends: $12 \\div 3 = 4$.\n\n**3. Let's check:** can you tell me in your own words what you understood?",
            "Thanks for sharing your answer. ✅ Your approach is right, but look at the **second step**: there seems to be a small calculation mistake.\n\n**Hint:** redo that operation slowly and compare. What do you get?",
            "Let's practise! 🎯\n\n**Exercise 1:** solve $3x - 5 = 10$.\n\nWrite your solution and the steps you followed. It's fine to make mistakes: that's how we learn.",
            "Nice try! What went well: you isolated the unknown. To improve: watch the signs.\n\n**Exercise 2** (a bit harder): solve $2(x + 4) = 18$.",
            "_Offline demo mode: these answers are examples. Add an Anthropic API key to talk to a real tutor._"),
    };
}
