using TutorForge.Core.Models;

namespace TutorForge.Core.Validation;

/// <summary>A validation problem: a stable code (translated by the UI) and a default English message.</summary>
public sealed record ValidationError(string Code, string Message);

public static class TutorRequestValidator
{
    public const string Empty = "Empty";
    public const string LastMustBeUser = "LastMustBeUser";
    public const string MessageTooLong = "MessageTooLong";
    public const string ConversationTooLong = "ConversationTooLong";
    public const string UnknownOption = "UnknownOption";

    public static IReadOnlyList<ValidationError> Check(TutorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ValidationError>();
        var messages = request.Messages ?? [];

        if (messages.Count == 0 || string.IsNullOrWhiteSpace(messages[^1].Content))
        {
            errors.Add(new(Empty, "Write a message first."));
            return errors;
        }

        if (messages[^1].Role != ChatRole.User)
        {
            errors.Add(new(LastMustBeUser, "The last message must come from the student."));
        }

        if (messages.Any(m => (m.Content?.Length ?? 0) > TutorRequest.MaxMessageLength))
        {
            errors.Add(new(MessageTooLong, $"A message can have at most {TutorRequest.MaxMessageLength} characters."));
        }

        if (messages.Count > TutorRequest.MaxMessages || messages.Sum(m => m.Content?.Length ?? 0) > TutorRequest.MaxTotalLength)
        {
            errors.Add(new(ConversationTooLong, "The conversation is too long. Start a new one."));
        }

        if (!Enum.IsDefined(request.Subject) || !Enum.IsDefined(request.Level)
            || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Language)
            || messages.Any(m => !Enum.IsDefined(m.Role)))
        {
            errors.Add(new(UnknownOption, "Unknown subject, level, mode, language or role."));
        }

        return errors;
    }
}
