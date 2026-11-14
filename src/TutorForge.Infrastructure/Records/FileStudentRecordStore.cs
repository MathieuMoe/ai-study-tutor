using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TutorForge.Core.Records;
using TutorForge.Infrastructure.Progress;

namespace TutorForge.Infrastructure.Records;

/// <summary>
/// Demo persistence of the student records: one readable JSON document per student (App_Data/records),
/// written atomically. Same interface a SQL Server repository would implement (see docs/database.sql).
/// </summary>
public sealed class FileStudentRecordStore : IStudentRecordStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true, // readable: the demo shows and edits this file
    };

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;

    public FileStudentRecordStore(IOptions<ProgressStoreOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Next to the progress folder: App_Data/progress -> App_Data/records
        var progress = Path.GetFullPath(options.Value.Directory);
        _directory = Path.Combine(Path.GetDirectoryName(progress) ?? progress, "records");
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_directory);
    }

    public async Task<StudentRecord?> GetAsync(Guid studentId, CancellationToken cancellationToken = default)
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

    public Task SaveAsync(StudentRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return UpdateAsync(record.StudentId, _ => record, cancellationToken);
    }

    public async Task UpdateAsync(Guid studentId, Func<StudentRecord?, StudentRecord> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var gate = Gate(studentId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updated = update(await ReadAsync(studentId, cancellationToken).ConfigureAwait(false)) with { StudentId = studentId };
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
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
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

    private async Task<StudentRecord?> ReadAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var path = PathFor(studentId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        try
        {
            return await JsonSerializer.DeserializeAsync<StudentRecord>(stream, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null; // corrupted document: start again rather than failing
        }
    }

    // The file name comes from a Guid formatted by us: no path traversal possible.
    private string PathFor(Guid studentId) => Path.Combine(_directory, studentId.ToString("N") + ".json");

    private SemaphoreSlim Gate(Guid studentId) => _locks.GetOrAdd(studentId, _ => new SemaphoreSlim(1, 1));
}
