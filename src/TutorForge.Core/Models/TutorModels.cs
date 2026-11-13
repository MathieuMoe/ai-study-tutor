namespace TutorForge.Core.Models;

public enum Subject
{
    Mathematics,
    PhysicsChemistry,
    Biology,
    HistoryGeography,
    LanguageLiterature,
    English,
    Programming,
    Other,
}

public enum Level
{
    Primary,
    Secondary,
    HighSchool,
    University,
}

/// <summary>How the tutor helps. The pedagogy changes a lot from one mode to another.</summary>
public enum TutorMode
{
    /// <summary>Guides with questions and hints, never gives the final answer straight away (Socratic method).</summary>
    Guide,

    /// <summary>Explains a concept clearly, with an example.</summary>
    Explain,

    /// <summary>Checks the student's answer and gives feedback.</summary>
    Check,

    /// <summary>Proposes a new exercise to practise, then corrects it.</summary>
    Practice,

    /// <summary>
    /// Adaptive level test: the tutor starts by itself, asks one question at a time, adjusts the difficulty
    /// to the answers, estimates the level and keeps updating it as the student progresses.
    /// </summary>
    Assess,
}

public enum TutorLanguage
{
    English,
    Spanish,
    French,
    Catalan,
}

public enum ChatRole
{
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>One turn of the conversation: the settings plus the whole history (the server keeps nothing).</summary>
public sealed record TutorRequest(
    IReadOnlyList<ChatMessage> Messages,
    Subject Subject = Subject.Mathematics,
    Level Level = Level.Secondary,
    TutorMode Mode = TutorMode.Guide,
    TutorLanguage Language = TutorLanguage.English,
    string? Topic = null)
{
    public const int MaxTopicLength = 120;

    public const int MaxMessageLength = 4_000;
    public const int MaxMessages = 40;
    public const int MaxTotalLength = 40_000;
}
