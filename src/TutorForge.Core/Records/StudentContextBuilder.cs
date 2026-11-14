using System.Globalization;
using System.Text;
using TutorForge.Core.Models;
using TutorForge.Core.Progress;

namespace TutorForge.Core.Records;

/// <summary>
/// Turns the student's data into a compact briefing for the AI tutor (a few hundred words), focused on the
/// current subject: objective, course topics, recent results and mistakes, level evolution, last sessions,
/// teacher notes. This is what lets the tutor continue where the student left off.
/// </summary>
public static class StudentContextBuilder
{
    public const int MaxLength = 3000;

    public static string? Build(StudentRecord? record, StudentProgress? progress, Subject subject, DateOnly today)
    {
        if (record is null && progress is null)
        {
            return null;
        }

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        if (record is not null)
        {
            var p = record.Profile;
            if (p.Objective is not null)
            {
                sb.Append("- Objective: ").AppendLine(p.Objective);
            }

            if (p.ExamDate is { } exam && exam >= today)
            {
                sb.Append("- Exam date: ").Append(exam.ToString("yyyy-MM-dd", inv)).Append(" (in ").Append((exam.DayNumber - today.DayNumber).ToString(inv)).AppendLine(" days)");
            }

            foreach (var e in record.Enrollments.Where(e => e.Subject == subject).Take(3))
            {
                sb.Append("- Course: ").Append(e.Course);
                if (e.Teacher is not null)
                {
                    sb.Append(" (teacher: ").Append(e.Teacher).Append(')');
                }

                sb.AppendLine();
                AppendTopics(sb, "  completed", e.Topics.Where(t => t.Status == TopicStatus.Completed));
                AppendTopics(sb, "  in progress", e.Topics.Where(t => t.Status == TopicStatus.InProgress));
                AppendTopics(sb, "  not started yet (syllabus order)", e.Topics.Where(t => t.Status == TopicStatus.NotStarted).Take(6));
            }

            var results = record.Assessments.Where(a => a.Subject == subject).OrderByDescending(a => a.Date).Take(5).ToList();
            if (results.Count > 0)
            {
                sb.AppendLine("- Recent results (score on 10):");
                foreach (var a in results)
                {
                    sb.Append("  ").Append(a.Date.ToString("yyyy-MM-dd", inv)).Append(' ').Append(a.Kind).Append(" \"").Append(a.Title).Append('"');
                    if (a.Topic is not null)
                    {
                        sb.Append(" [").Append(a.Topic).Append(']');
                    }

                    sb.Append(": ").Append(a.OnTen.ToString("0.#", inv));
                    if (a.Mistakes.Count > 0)
                    {
                        sb.Append(" — mistakes: ").Append(string.Join("; ", a.Mistakes.Take(3)));
                    }

                    sb.AppendLine();
                }
            }

            var sessions = record.Activity.Where(a => a.Subject == subject).OrderByDescending(a => a.Start).Take(5).ToList();
            if (sessions.Count > 0)
            {
                sb.AppendLine("- Last study sessions:");
                foreach (var s in sessions)
                {
                    sb.Append("  ").Append(s.Start.ToString("yyyy-MM-dd", inv)).Append(' ').Append(s.Kind);
                    if (s.Mode is not null)
                    {
                        sb.Append('/').Append(s.Mode);
                    }

                    if (s.Topic is not null)
                    {
                        sb.Append(" \"").Append(s.Topic).Append('"');
                    }

                    sb.Append(", ").Append(s.Minutes.ToString(inv)).Append(" min");
                    if (s.LevelBefore is not null || s.LevelAfter is not null)
                    {
                        sb.Append(", level ").Append(s.LevelBefore?.ToString(inv) ?? "?").Append('→').Append(s.LevelAfter?.ToString(inv) ?? "?");
                    }

                    if (s.Summary is not null)
                    {
                        sb.Append(", started with: \"").Append(s.Summary).Append('"');
                    }

                    sb.AppendLine();
                }
            }

            foreach (var n in record.TeacherNotes.Take(3))
            {
                sb.Append("- Teacher note (").Append(n.Date.ToString("yyyy-MM-dd", inv)).Append("): ").AppendLine(n.Text);
            }

            var goals = record.Goals.Where(g => !g.Done).Take(3).ToList();
            if (goals.Count > 0)
            {
                sb.Append("- Pending goals: ").AppendLine(string.Join("; ", goals.Select(g => g.Text)));
            }
        }

        if (progress?.Subjects.GetValueOrDefault(subject) is { } sp && sp.LevelHistory.Count > 0)
        {
            var first = sp.LevelHistory[0];
            var last = sp.LevelHistory[^1];
            sb.Append("- Level estimated by the tutor in this subject: ").Append(last.Level.ToString(inv)).Append("/10");
            if (sp.LevelHistory.Count > 1)
            {
                sb.Append(" (it was ").Append(first.Level.ToString(inv)).Append("/10 on ").Append(first.At.ToString("yyyy-MM-dd", inv)).Append(')');
            }

            sb.AppendLine();
        }

        if (sb.Length == 0)
        {
            return null;
        }

        var text = sb.ToString();
        return text.Length > MaxLength ? text[..MaxLength] + "…" : text;
    }

    private static void AppendTopics(StringBuilder sb, string label, IEnumerable<TopicProgress> topics)
    {
        var list = topics.Select(t => t.Mastery is { } m ? $"{t.Name} ({m.ToString(CultureInfo.InvariantCulture)}/10)" : t.Name).ToList();
        if (list.Count > 0)
        {
            sb.Append(label).Append(": ").AppendLine(string.Join(", ", list));
        }
    }
}
