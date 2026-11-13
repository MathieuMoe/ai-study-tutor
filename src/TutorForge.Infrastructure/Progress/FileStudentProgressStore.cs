using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TutorForge.Core.Progress;

namespace TutorForge.Infrastructure.Progress;

public sealed class ProgressStoreOptions
{
    public const string SectionName = "Progress";

    /// <summary>Folder for the JSON files (relative to the app's content root). Excluded from git.</summary>
    public string Directory { get; set; } = "App_Data/progress";

    /// <summary>Students inactive for longer than this are deleted (data minimisation).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);
}

/// <summary>
/// Demo persistence: one JSON document per anonymous student, written atomically.
/// It behaves like the future database repository (same interface, same upsert semantics),
/// so swapping it for a SQL Server implementation only changes the DI registration.
/// </summary>
public sealed class FileStudentProgressStore : IStudentProgressStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;

    public FileStudentProgressStore(IOptions<ProgressStoreOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _directory = Path.GetFullPath(options.Value.Directory);
        _timeProvider = timeProvider;
        System.IO.Directory.CreateDirectory(_directory);
    }

    public async Task<StudentProgress?> GetAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var gate = Gate(studentId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(studentId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveSubjectAsync(Guid studentId, SubjectProgress progress, CancellationToken cancellationToken = default)
    {
        var gate = Gate(studentId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadAsync(studentId, cancellationToken).ConfigureAwait(false);
            var updated = ProgressMerge.WithSubject(existing, studentId, progress, _timeProvider.GetUtcNow());

            // Atomic write: temp file + rename, so a crash never leaves a half-written document.
            var path = PathFor(studentId);
            var temp = path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, updated, Json, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var gate = Gate(studentId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(PathFor(studentId));
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<int> PurgeInactiveAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var limit = _timeProvider.GetUtcNow().UtcDateTime - olderThan;
        var deleted = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) < limit)
            {
                File.Delete(file);
                deleted++;
            }
        }

        return Task.FromResult(deleted);
    }

    private async Task<StudentProgress?> ReadAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var path = PathFor(studentId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        try
        {
            return await JsonSerializer.DeserializeAsync<StudentProgress>(stream, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null; // corrupted document: start again rather than failing
        }
    }

    // The file name comes from a Guid formatted by us ("N" = 32 hex digits): no path traversal possible.
    private string PathFor(Guid studentId) => Path.Combine(_directory, studentId.ToString("N") + ".json");

    private SemaphoreSlim Gate(Guid studentId) => _locks.GetOrAdd(studentId, _ => new SemaphoreSlim(1, 1));
}
