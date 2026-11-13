using TutorForge.Core.Models;

namespace TutorForge.Core.Progress;

/// <summary>
/// Persistence of the students' progress. The demo uses JSON files
/// (<c>FileStudentProgressStore</c>); a production version would implement the same interface
/// with SQL Server / EF Core using the schema in docs/database.sql. Nothing else has to change.
/// </summary>
public interface IStudentProgressStore
{
    Task<StudentProgress?> GetAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the progress of one subject and marks it as the last subject used.</summary>
    Task SaveSubjectAsync(Guid studentId, SubjectProgress progress, CancellationToken cancellationToken = default);

    /// <summary>Deletes everything about the student ("forget me").</summary>
    Task DeleteAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Deletes students inactive for longer than <paramref name="olderThan"/> (data retention). Returns the number deleted.</summary>
    Task<int> PurgeInactiveAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe in-memory store, used by tests and usable for local development.</summary>
public sealed class InMemoryStudentProgressStore(TimeProvider timeProvider) : IStudentProgressStore
{
    private readonly Dictionary<Guid, StudentProgress> _students = [];
    private readonly Lock _lock = new();

    public InMemoryStudentProgressStore() : this(TimeProvider.System) { }

    public Task<StudentProgress?> GetAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_students.GetValueOrDefault(studentId));
        }
    }

    public Task SaveSubjectAsync(Guid studentId, SubjectProgress progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            _students[studentId] = ProgressMerge.WithSubject(_students.GetValueOrDefault(studentId), studentId, progress, now);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _students.Remove(studentId);
        }

        return Task.CompletedTask;
    }

    public Task<int> PurgeInactiveAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var limit = timeProvider.GetUtcNow() - olderThan;
            var old = _students.Where(s => s.Value.UpdatedAt < limit).Select(s => s.Key).ToList();
            old.ForEach(id => _students.Remove(id));
            return Task.FromResult(old.Count);
        }
    }
}

/// <summary>Shared upsert logic for the stores (equivalent to an UPSERT on Students + SubjectProgress).</summary>
public static class ProgressMerge
{
    public static StudentProgress WithSubject(StudentProgress? existing, Guid studentId, SubjectProgress progress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var subjects = new Dictionary<Subject, SubjectProgress>(existing?.Subjects ?? new Dictionary<Subject, SubjectProgress>())
        {
            [progress.Subject] = progress,
        };
        return new StudentProgress(studentId, progress.Subject, subjects, existing?.CreatedAt ?? now, now);
    }
}
