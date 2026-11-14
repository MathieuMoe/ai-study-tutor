using TutorForge.Core.Models;

namespace TutorForge.Core.Records;

/// <summary>Pure rules on the record: sanitising imported JSON and logging tutor sessions.</summary>
public static class RecordRules
{
    /// <summary>Consecutive tutor answers closer than this belong to the same session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Makes an imported or edited record safe: forces the student id, trims texts, clamps scores and list sizes,
    /// drops empty items. The record comes from the user, so nothing is trusted.
    /// </summary>
    public static StudentRecord Sanitize(StudentRecord? record, Guid studentId, DateTimeOffset now)
    {
        if (record is null)
        {
            return StudentRecord.Empty(studentId, now);
        }

        var p = record.Profile ?? new StudentProfile(null, null, null, null, null, null);
        var profile = new StudentProfile(Text(p.DisplayName, 60), p.SchoolLevel, p.Language, Text(p.Objective), p.ExamDate, Text(p.Academy, 120));

        var enrollments = (record.Enrollments ?? [])
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Course))
            .Take(StudentRecord.MaxEnrollments)
            .Select(e => new Enrollment(Text(e.Course, 150)!, e.Subject, Text(e.Teacher, 100),
                (e.Topics ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name)).Take(StudentRecord.MaxTopicsPerEnrollment)
                    .Select(t => new TopicProgress(Text(t.Name, 150)!, t.Status, Clamp(t.Mastery), t.LastActivity)).ToList()))
            .ToList();

        var assessments = (record.Assessments ?? [])
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Title) && a.MaxScore > 0)
            .OrderByDescending(a => a.Date)
            .Take(StudentRecord.MaxAssessments)
            .Select(a => new Assessment(a.Date, a.Subject, Text(a.Title, 150)!, Text(a.Topic, 150), Math.Clamp(a.Score, 0, a.MaxScore),
                Math.Min(a.MaxScore, 1000), a.Kind, (a.Mistakes ?? []).Select(m => Text(m)).OfType<string>().Take(10).ToList()))
            .ToList();

        var activity = (record.Activity ?? [])
            .Where(a => a is not null && a.End >= a.Start)
            .OrderByDescending(a => a.Start)
            .Take(StudentRecord.MaxActivity)
            .Select(a => a with { Topic = Text(a.Topic, 150), Summary = Text(a.Summary), Turns = Math.Clamp(a.Turns, 0, 1000), LevelBefore = Clamp(a.LevelBefore), LevelAfter = Clamp(a.LevelAfter) })
            .ToList();

        var goals = (record.Goals ?? []).Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Text)).Take(StudentRecord.MaxGoals)
            .Select(g => g with { Text = Text(g.Text)! }).ToList();
        var notes = (record.TeacherNotes ?? []).Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Text)).OrderByDescending(n => n.Date).Take(StudentRecord.MaxNotes)
            .Select(n => n with { Author = Text(n.Author, 100) ?? "-", Text = Text(n.Text, 600)! }).ToList();

        return new StudentRecord(studentId, profile, enrollments, assessments, activity, goals, notes, now);
    }

    /// <summary>
    /// Logs one tutor answer: extends the current session of the same subject, or opens a new one.
    /// Also marks the matching course topic as in progress and updates its mastery with the tutor's estimate.
    /// </summary>
    public static StudentRecord TrackTutorTurn(StudentRecord? record, Guid studentId, TutorRequest request, int? levelAfter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        record ??= StudentRecord.Empty(studentId, now);
        var topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        var activity = record.Activity.ToList();
        var latest = activity.Where(a => a.Kind == ActivityKind.TutorSession).OrderByDescending(a => a.End).FirstOrDefault();

        if (latest is not null && latest.Subject == request.Subject && now - latest.End <= SessionGap
            && string.Equals(latest.Topic, topic, StringComparison.OrdinalIgnoreCase))
        {
            activity[activity.IndexOf(latest)] = latest with
            {
                End = now,
                Turns = latest.Turns + 1,
                Mode = request.Mode,
                LevelAfter = levelAfter ?? latest.LevelAfter,
            };
        }
        else
        {
            var firstQuestion = request.Messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Content;
            var previousLevel = activity.Where(a => a.Subject == request.Subject && a.LevelAfter is not null).OrderByDescending(a => a.End).FirstOrDefault()?.LevelAfter;
            activity.Insert(0, new ActivityEntry(now, now, ActivityKind.TutorSession, request.Subject, topic, request.Mode, 1,
                previousLevel, levelAfter ?? previousLevel, Text(firstQuestion, 120)));
        }

        var enrollments = record.Enrollments.Select(e => e.Subject != request.Subject || topic is null ? e : e with
        {
            Topics = e.Topics.Select(t => !Same(t.Name, topic) ? t : t with
            {
                Status = t.Status == TopicStatus.Completed ? t.Status : TopicStatus.InProgress,
                Mastery = levelAfter ?? t.Mastery,
                LastActivity = DateOnly.FromDateTime(now.UtcDateTime),
            }).ToList(),
        }).ToList();

        return record with
        {
            Activity = activity.OrderByDescending(a => a.Start).Take(StudentRecord.MaxActivity).ToList(),
            Enrollments = enrollments,
            UpdatedAt = now,
        };
    }

    /// <summary>"ecuaciones" matches "Tema 3. Ecuaciones de primer grado": the tutor topic is usually shorter.</summary>
    public static bool Same(string a, string b)
    {
        var x = a.Trim();
        var y = b.Trim();
        return x.Length > 2 && y.Length > 2
            && (x.Contains(y, StringComparison.OrdinalIgnoreCase) || y.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static int? Clamp(int? value) => value is null ? null : Math.Clamp(value.Value, 0, 10);

    private static string? Text(string? value, int max = StudentRecord.MaxText)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ReplaceLineEndings(" ");
        return text.Length > max ? text[..max] : text;
    }
}
