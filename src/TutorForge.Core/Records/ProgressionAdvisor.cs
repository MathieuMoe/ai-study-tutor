using TutorForge.Core.Models;
using TutorForge.Core.Progress;

namespace TutorForge.Core.Records;

/// <summary>A next step for the student. The interface translates the code and starts the tutor with mode and topic.</summary>
public sealed record Recommendation(string Code, Subject Subject, string? Topic, TutorMode Mode, int Priority, string? Detail = null, double? Value = null);

/// <summary>
/// The study guide: rule-based next steps from the record and the progress (no AI needed, explainable):
/// review weak results, continue topics in progress, assess subjects without a level, start the next topic,
/// come back after a break, and mock exams when the exam is close.
/// </summary>
public static class ProgressionAdvisor
{
    public const int MaxRecommendations = 6;

    /// <summary>The tutor's latest level estimate in a subject: saved progress first, then the logged sessions.</summary>
    public static int? LatestLevel(StudentRecord? record, StudentProgress? progress, Subject subject) =>
        progress?.Subjects.GetValueOrDefault(subject)?.EstimatedLevel
        ?? record?.Activity.Where(a => a.Subject == subject && a.LevelAfter is not null).OrderByDescending(a => a.End).FirstOrDefault()?.LevelAfter;

    public static IReadOnlyList<Recommendation> Recommend(StudentRecord? record, StudentProgress? progress, DateOnly today)
    {
        var list = new List<Recommendation>();
        var enrollments = record?.Enrollments ?? [];

        // 1. Weak recent results (< 6/10, last 45 days): review that topic first
        foreach (var a in (record?.Assessments ?? []).Where(a => a.OnTen < 6 && today.DayNumber - a.Date.DayNumber <= 45)
                     .OrderBy(a => a.OnTen).Take(3))
        {
            list.Add(new Recommendation("ReviewWeak", a.Subject, a.Topic ?? a.Title, TutorMode.Practice, 1, a.Title, a.OnTen));
        }

        // 2. Topics in progress with low mastery: continue
        foreach (var (e, t) in enrollments.SelectMany(e => e.Topics.Where(t => t.Status == TopicStatus.InProgress && (t.Mastery ?? 0) < 7).Select(t => (e, t))).Take(3))
        {
            list.Add(new Recommendation("Continue", e.Subject, t.Name, TutorMode.Guide, 2, e.Course, t.Mastery));
        }

        // 3. Enrolled subjects never assessed by the tutor: level test
        foreach (var subject in enrollments.Select(e => e.Subject).Distinct()
                     .Where(s => LatestLevel(record, progress, s) is null))
        {
            list.Add(new Recommendation("AssessLevel", subject, null, TutorMode.Assess, 3));
        }

        // 4. Next topic of the syllabus not started yet
        foreach (var e in enrollments)
        {
            if (e.Topics.FirstOrDefault(t => t.Status == TopicStatus.NotStarted) is { } next)
            {
                list.Add(new Recommendation("StartNext", e.Subject, next.Name, TutorMode.Explain, 4, e.Course));
            }
        }

        // 5. Subjects not practised for a week: come back
        foreach (var group in (record?.Activity ?? []).GroupBy(a => a.Subject))
        {
            var days = today.DayNumber - DateOnly.FromDateTime(group.Max(a => a.End).UtcDateTime).DayNumber;
            if (days >= 7)
            {
                list.Add(new Recommendation("ComeBack", group.Key, group.OrderByDescending(a => a.End).First().Topic, TutorMode.Practice, 3, null, days));
            }
        }

        // 6. Exam in the next 30 days: mock exams in the enrolled subjects
        if (record?.Profile.ExamDate is { } exam && exam >= today && exam.DayNumber - today.DayNumber <= 30)
        {
            foreach (var subject in enrollments.Select(e => e.Subject).Distinct().Take(2))
            {
                list.Add(new Recommendation("ExamSoon", subject, null, TutorMode.Practice, 0, null, exam.DayNumber - today.DayNumber));
            }
        }

        // One recommendation per subject+topic, most urgent first
        return list
            .GroupBy(r => (r.Subject, Topic: r.Topic?.ToUpperInvariant()))
            .Select(g => g.OrderBy(r => r.Priority).First())
            .OrderBy(r => r.Priority)
            .Take(MaxRecommendations)
            .ToList();
    }
}
