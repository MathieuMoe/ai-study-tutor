using TutorForge.Core.Models;

namespace TutorForge.Core.Records;

/// <summary>
/// A realistic demo student, like the data a school platform would have: two courses, results with mistakes,
/// study sessions over the last weeks, goals and teacher notes. Dates are relative to today.
/// </summary>
public static class SampleRecord
{
    public static StudentRecord Create(Guid studentId, TutorLanguage language, DateTimeOffset now)
    {
        var t = Texts.For(language);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateOnly Day(int daysAgo) => today.AddDays(-daysAgo);
        DateTimeOffset At(int daysAgo, int hour) => new DateTimeOffset(now.UtcDateTime.Date.AddDays(-daysAgo).AddHours(hour), TimeSpan.Zero);

        var maths = new Enrollment(t.MathCourse, Subject.Mathematics, t.MathTeacher,
        [
            new(t.MathTopics[0], TopicStatus.Completed, 8, Day(20)),
            new(t.MathTopics[1], TopicStatus.Completed, 7, Day(14)),
            new(t.MathTopics[2], TopicStatus.InProgress, 4, Day(3)),
            new(t.MathTopics[3], TopicStatus.NotStarted, null, null),
            new(t.MathTopics[4], TopicStatus.NotStarted, null, null),
            new(t.MathTopics[5], TopicStatus.NotStarted, null, null),
        ]);
        var biology = new Enrollment(t.BioCourse, Subject.Biology, t.BioTeacher,
        [
            new(t.BioTopics[0], TopicStatus.Completed, 8, Day(18)),
            new(t.BioTopics[1], TopicStatus.InProgress, 6, Day(9)),
            new(t.BioTopics[2], TopicStatus.NotStarted, null, null),
            new(t.BioTopics[3], TopicStatus.NotStarted, null, null),
        ]);

        return new StudentRecord(
            studentId,
            new StudentProfile(t.Name, Level.Secondary, language, t.Objective, today.AddDays(45), t.Academy),
            [maths, biology],
            [
                new(Day(3), Subject.Mathematics, t.Tests[0], t.MathTopics[2], 4, 10, AssessmentKind.Test, t.MathMistakes),
                new(Day(10), Subject.Mathematics, t.Tests[1], t.MathTopics[1], 7, 10, AssessmentKind.Test, []),
                new(Day(21), Subject.Mathematics, t.Tests[2], t.MathTopics[0], 16, 20, AssessmentKind.Exam, []),
                new(Day(9), Subject.Biology, t.Tests[3], t.BioTopics[1], 5.5, 10, AssessmentKind.Test, t.BioMistakes),
                new(Day(18), Subject.Biology, t.Tests[4], t.BioTopics[0], 8, 10, AssessmentKind.Homework, []),
            ],
            [
                new(At(3, 17), At(3, 17).AddMinutes(35), ActivityKind.TutorSession, Subject.Mathematics, t.MathTopics[2], TutorMode.Practice, 9, 3, 4, t.Session1),
                new(At(5, 16), At(5, 16).AddMinutes(50), ActivityKind.Lesson, Subject.Mathematics, t.MathTopics[2], null, 0, null, null, null),
                new(At(9, 18), At(9, 18).AddMinutes(25), ActivityKind.TutorSession, Subject.Biology, t.BioTopics[1], TutorMode.Explain, 6, 5, 6, t.Session2),
                new(At(12, 17), At(12, 17).AddMinutes(40), ActivityKind.Test, Subject.Mathematics, t.MathTopics[1], null, 0, null, null, null),
                new(At(14, 18), At(14, 18).AddMinutes(30), ActivityKind.TutorSession, Subject.Mathematics, t.MathTopics[1], TutorMode.Assess, 7, null, 6, t.Session3),
            ],
            [new(t.Goals[0], today.AddDays(14), false), new(t.Goals[1], today.AddDays(40), false), new(t.Goals[2], Day(15), true)],
            [new(Day(4), t.MathTeacher, t.Notes[0]), new(Day(11), t.BioTeacher, t.Notes[1])],
            now);
    }

    private sealed record Texts(
        string Name, string Objective, string Academy, string MathCourse, string MathTeacher, string[] MathTopics,
        string BioCourse, string BioTeacher, string[] BioTopics, string[] Tests, string[] MathMistakes, string[] BioMistakes,
        string Session1, string Session2, string Session3, string[] Goals, string[] Notes)
    {
        public static Texts For(TutorLanguage language) => language switch
        {
            TutorLanguage.Spanish => new(
                "Alex (demo)", "Aprobar Matemáticas y Biología de 2º ESO con al menos un 7", "Academia Demo",
                "Matemáticas 2º ESO", "Prof. García", ["Números enteros", "Fracciones y decimales", "Ecuaciones de primer grado", "Proporcionalidad", "Geometría plana", "Funciones"],
                "Biología y Geología 2º ESO", "Prof. Martín", ["La célula", "Los seres vivos y su clasificación", "Las plantas", "Los ecosistemas"],
                ["Test de ecuaciones", "Test de fracciones", "Examen trimestral: números enteros", "Test de clasificación de seres vivos", "Trabajo: la célula"],
                ["Errores de signo al pasar términos de un lado a otro", "No comprueba la solución sustituyendo"], ["Confunde vertebrados e invertebrados en los ejemplos"],
                "Ecuaciones con paréntesis", "¿Cómo se clasifican los seres vivos?", "Test de nivel de fracciones",
                ["Sacar al menos un 6 en el próximo test de ecuaciones", "Terminar el temario de Biología antes del examen", "Repasar fracciones"],
                ["Le cuesta despejar la incógnita cuando hay paréntesis: practicar paso a paso.", "Muy buena actitud; necesita ejemplos concretos para memorizar la clasificación."]),
            TutorLanguage.French => new(
                "Alex (démo)", "Réussir maths et SVT de 5e avec au moins 14/20", "Académie Démo",
                "Mathématiques 5e", "M. Garcia", ["Nombres relatifs", "Fractions et décimaux", "Équations du premier degré", "Proportionnalité", "Géométrie plane", "Fonctions"],
                "SVT 5e", "Mme Martin", ["La cellule", "Classification du vivant", "Les plantes", "Les écosystèmes"],
                ["Test sur les équations", "Test sur les fractions", "Contrôle trimestriel : nombres relatifs", "Test sur la classification du vivant", "Exposé : la cellule"],
                ["Erreurs de signe en changeant de membre", "Ne vérifie pas la solution"], ["Confond vertébrés et invertébrés dans les exemples"],
                "Équations avec parenthèses", "Comment classe-t-on les êtres vivants ?", "Test de niveau sur les fractions",
                ["Avoir au moins 12/20 au prochain test d'équations", "Finir le programme de SVT avant l'examen", "Réviser les fractions"],
                ["A du mal à isoler l'inconnue avec des parenthèses : s'entraîner étape par étape.", "Très bonne attitude ; a besoin d'exemples concrets pour mémoriser la classification."]),
            TutorLanguage.Catalan => new(
                "Alex (demo)", "Aprovar Matemàtiques i Biologia de 2n d'ESO amb almenys un 7", "Acadèmia Demo",
                "Matemàtiques 2n ESO", "Prof. Garcia", ["Nombres enters", "Fraccions i decimals", "Equacions de primer grau", "Proporcionalitat", "Geometria plana", "Funcions"],
                "Biologia i Geologia 2n ESO", "Prof. Martín", ["La cèl·lula", "Els éssers vius i la seva classificació", "Les plantes", "Els ecosistemes"],
                ["Test d'equacions", "Test de fraccions", "Examen trimestral: nombres enters", "Test de classificació dels éssers vius", "Treball: la cèl·lula"],
                ["Errors de signe en passar termes d'un costat a l'altre", "No comprova la solució"], ["Confon vertebrats i invertebrats als exemples"],
                "Equacions amb parèntesis", "Com es classifiquen els éssers vius?", "Test de nivell de fraccions",
                ["Treure almenys un 6 al pròxim test d'equacions", "Acabar el temari de Biologia abans de l'examen", "Repassar fraccions"],
                ["Li costa aïllar la incògnita quan hi ha parèntesis: practicar pas a pas.", "Molt bona actitud; necessita exemples concrets per memoritzar la classificació."]),
            _ => new(
                "Alex (demo)", "Pass Year 8 Maths and Biology with at least 7/10", "Demo Academy",
                "Mathematics Year 8", "Mr Garcia", ["Integers", "Fractions and decimals", "Linear equations", "Proportionality", "Plane geometry", "Functions"],
                "Biology Year 8", "Ms Martin", ["The cell", "Living things and classification", "Plants", "Ecosystems"],
                ["Equations test", "Fractions test", "Term exam: integers", "Classification of living things test", "Project: the cell"],
                ["Sign errors when moving terms across the equals sign", "Does not check the solution"], ["Mixes up vertebrates and invertebrates in examples"],
                "Equations with brackets", "How are living things classified?", "Fractions level test",
                ["Score at least 6/10 in the next equations test", "Finish the Biology syllabus before the exam", "Review fractions"],
                ["Finds it hard to isolate x when there are brackets: practise step by step.", "Great attitude; needs concrete examples to memorise the classification."]),
        };
    }
}
