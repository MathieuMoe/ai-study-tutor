using TutorForge.Core.Models;

namespace TutorForge.Core.Progress;

// The progress model is shaped like database tables (see docs/database.sql):
// Students 1─N SubjectProgress 1─N LevelHistory / ConversationMessages.

/// <summary>An anonymous student (identified by a random id in a cookie, no personal data).</summary>
public sealed record StudentProgress(
    Guid StudentId,
    Subject? LastSubject,
    IReadOnlyDictionary<Subject, SubjectProgress> Subjects,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Where the student is in one subject: settings, estimated level and the current conversation.</summary>
public sealed record SubjectProgress(
    Subject Subject,
    string? Topic,
    Level SchoolLevel,
    TutorMode Mode,
    TutorLanguage Language,
    int? EstimatedLevel,
    IReadOnlyList<LevelPoint> LevelHistory,
    IReadOnlyList<ChatMessage> Messages,
    DateTimeOffset UpdatedAt);

/// <summary>One estimate of the level (0-10) at a given time. The history shows how the student grows.</summary>
public sealed record LevelPoint(int Level, DateTimeOffset At);

/// <summary>What the browser sends after each answer. The server computes history and timestamps.</summary>
public sealed record SaveProgressRequest(
    string? Topic,
    Level SchoolLevel,
    TutorMode Mode,
    TutorLanguage Language,
    int? EstimatedLevel,
    IReadOnlyList<ChatMessage> Messages)
{
    public const int MaxLevelHistory = 50;
}
