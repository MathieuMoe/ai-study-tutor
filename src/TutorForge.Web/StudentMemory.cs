using System.Text.RegularExpressions;
using TutorForge.Core.Models;
using TutorForge.Core.Progress;
using TutorForge.Core.Records;

namespace TutorForge.Web;

/// <summary>The tutor's memory: reads the student's data for the AI and logs each answer in the record.</summary>
public sealed partial class StudentMemory(IStudentRecordStore records, ProgressService progress, TimeProvider clock, ILogger<StudentMemory> logger)
{
    public async Task<string?> ContextAsync(Guid studentId, Subject subject, CancellationToken cancellationToken)
    {
        try
        {
            var record = await records.GetAsync(studentId, cancellationToken);
            var saved = await progress.GetAsync(studentId, cancellationToken);
            return StudentContextBuilder.Build(record, saved, subject, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(logger, ex);
            return null; // the tutor still answers, just without memory
        }
    }

    public async Task TrackAsync(Guid studentId, TutorRequest request, string answer, CancellationToken cancellationToken)
    {
        var levels = LevelTag().Matches(answer ?? string.Empty);
        int? level = levels.Count > 0 && int.TryParse(levels[^1].Groups[1].Value, out var n) ? Math.Clamp(n, 0, 10) : null;
        try
        {
            await records.UpdateAsync(studentId, record => RecordRules.TrackTutorTurn(record, studentId, request, level, clock.GetUtcNow()), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(logger, ex);
        }
    }

    [GeneratedRegex(@"\[\[LEVEL:(\d{1,2})\]\]")]
    private static partial Regex LevelTag();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Student memory unavailable")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
