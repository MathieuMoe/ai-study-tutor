using TutorForge.Core.Models;
using TutorForge.Core.Validation;

namespace TutorForge.Core.Progress;

/// <summary>
/// Application service for the student's progress: validates what the browser sends,
/// keeps the level history (appends a point only when the level changes) and saves through the store.
/// </summary>
public sealed class ProgressService(IStudentProgressStore store, TimeProvider timeProvider)
{
    public Task<StudentProgress?> GetAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        store.GetAsync(studentId, cancellationToken);

    public Task DeleteAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        store.DeleteAsync(studentId, cancellationToken);

    /// <summary>Validates and saves. Returns the validation errors (empty when saved).</summary>
    public async Task<IReadOnlyList<ValidationError>> SaveAsync(
        Guid studentId, Subject subject, SaveProgressRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = Validate(subject, request);
        if (errors.Count > 0)
        {
            return errors;
        }

        var now = timeProvider.GetUtcNow();
        var existing = (await store.GetAsync(studentId, cancellationToken).ConfigureAwait(false))?.Subjects.GetValueOrDefault(subject);

        var history = existing?.LevelHistory.ToList() ?? [];
        if (request.EstimatedLevel is { } level && (history.Count == 0 || history[^1].Level != level))
        {
            history.Add(new LevelPoint(level, now));
        }

        if (history.Count > SaveProgressRequest.MaxLevelHistory)
        {
            history = history[^SaveProgressRequest.MaxLevelHistory..];
        }

        var progress = new SubjectProgress(
            subject,
            string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim(),
            request.SchoolLevel,
            request.Mode,
            request.Language,
            request.EstimatedLevel ?? existing?.EstimatedLevel,
            history,
            request.Messages ?? [],
            now);

        await store.SaveSubjectAsync(studentId, progress, cancellationToken).ConfigureAwait(false);
        return [];
    }

    private static List<ValidationError> Validate(Subject subject, SaveProgressRequest request)
    {
        var errors = new List<ValidationError>();
        var messages = request.Messages ?? [];

        if (!Enum.IsDefined(subject) || !Enum.IsDefined(request.SchoolLevel) || !Enum.IsDefined(request.Mode)
            || !Enum.IsDefined(request.Language) || messages.Any(m => !Enum.IsDefined(m.Role)))
        {
            errors.Add(new(TutorRequestValidator.UnknownOption, "Unknown subject, level, mode, language or role."));
        }

        if (request.EstimatedLevel is < 0 or > 10)
        {
            errors.Add(new("InvalidLevel", "The level must be between 0 and 10."));
        }

        if ((request.Topic?.Length ?? 0) > TutorRequest.MaxTopicLength)
        {
            errors.Add(new(TutorRequestValidator.TopicTooLong, $"The topic can have at most {TutorRequest.MaxTopicLength} characters."));
        }

        if (messages.Any(m => (m.Content?.Length ?? 0) > TutorRequest.MaxMessageLength)
            || messages.Count > TutorRequest.MaxMessages
            || messages.Sum(m => m.Content?.Length ?? 0) > TutorRequest.MaxTotalLength)
        {
            errors.Add(new(TutorRequestValidator.ConversationTooLong, "The conversation is too long to be saved."));
        }

        return errors;
    }
}
