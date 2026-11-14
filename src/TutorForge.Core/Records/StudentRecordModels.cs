using TutorForge.Core.Models;

namespace TutorForge.Core.Records;

// The student record ("expediente"): what a learning platform knows about a student, as one JSON document.
// It is shaped like database tables (see docs/database.sql): Students 1─1 Profile, 1─N Enrollments 1─N Topics,
// 1─N Assessments, 1─N Activity, 1─N Goals, 1─N TeacherNotes. The tutor reads it to personalise its answers
// and writes to Activity after each answer, so it never forgets what was done.

public sealed record StudentRecord(
    Guid StudentId,
    StudentProfile Profile,
    IReadOnlyList<Enrollment> Enrollments,
    IReadOnlyList<Assessment> Assessments,
    IReadOnlyList<ActivityEntry> Activity,
    IReadOnlyList<Goal> Goals,
    IReadOnlyList<TeacherNote> TeacherNotes,
    DateTimeOffset UpdatedAt)
{
    public const int MaxEnrollments = 20;
    public const int MaxTopicsPerEnrollment = 60;
    public const int MaxAssessments = 300;
    public const int MaxActivity = 300;
    public const int MaxGoals = 20;
    public const int MaxNotes = 50;
    public const int MaxText = 300;

    public static StudentRecord Empty(Guid studentId, DateTimeOffset now) =>
        new(studentId, new StudentProfile(null, null, null, null, null, null), [], [], [], [], [], now);
}

/// <summary>No personal data is needed: the display name is optional (a nickname is enough).</summary>
public sealed record StudentProfile(
    string? DisplayName,
    Level? SchoolLevel,
    TutorLanguage? Language,
    string? Objective,
    DateOnly? ExamDate,
    string? Academy);

/// <summary>A course the student follows, with the topics of its syllabus and where the student is in each.</summary>
public sealed record Enrollment(string Course, Subject Subject, string? Teacher, IReadOnlyList<TopicProgress> Topics);

public enum TopicStatus
{
    NotStarted,
    InProgress,
    Completed,
}

/// <param name="Mastery">Estimated mastery 0-10 (from tests or from the tutor's level estimate).</param>
public sealed record TopicProgress(string Name, TopicStatus Status, int? Mastery, DateOnly? LastActivity);

public enum AssessmentKind
{
    Test,
    Exam,
    Homework,
    MockExam,
    LevelTest,
}

/// <summary>A graded result (test, exam, homework...) with the mistakes made, if known.</summary>
public sealed record Assessment(
    DateOnly Date,
    Subject Subject,
    string Title,
    string? Topic,
    double Score,
    double MaxScore,
    AssessmentKind Kind,
    IReadOnlyList<string> Mistakes)
{
    /// <summary>Score on 10, whatever the scale of the test.</summary>
    public double OnTen => MaxScore <= 0 ? 0 : Math.Round(Score / MaxScore * 10, 1);
}

public enum ActivityKind
{
    TutorSession,
    Lesson,
    Test,
    Video,
    Assignment,
}

/// <summary>
/// One study session. Tutor sessions are written automatically: consecutive answers in the same subject
/// within 30 minutes are merged into one entry (turns, level before and after).
/// </summary>
public sealed record ActivityEntry(
    DateTimeOffset Start,
    DateTimeOffset End,
    ActivityKind Kind,
    Subject Subject,
    string? Topic,
    TutorMode? Mode,
    int Turns,
    int? LevelBefore,
    int? LevelAfter,
    string? Summary)
{
    public int Minutes => Math.Max(1, (int)Math.Round((End - Start).TotalMinutes));
}

public sealed record Goal(string Text, DateOnly? Target, bool Done);

public sealed record TeacherNote(DateOnly Date, string Author, string Text);

/// <summary>Persistence of the student records (JSON files in the demo, a database in production).</summary>
public interface IStudentRecordStore
{
    Task<StudentRecord?> GetAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task SaveAsync(StudentRecord record, CancellationToken cancellationToken = default);

    /// <summary>Read-modify-write under the student's lock (used to append activity safely).</summary>
    Task UpdateAsync(Guid studentId, Func<StudentRecord?, StudentRecord> update, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<int> PurgeInactiveAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}
