using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TutorForge.Core.Progress;

namespace TutorForge.Infrastructure.Progress;

/// <summary>Once a day, deletes the progress of students inactive for longer than the retention period.</summary>
public sealed partial class ProgressCleanupService(
    IStudentProgressStore store,
    IOptions<ProgressStoreOptions> options,
    ILogger<ProgressCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var deleted = await store.PurgeInactiveAsync(options.Value.Retention, stoppingToken).ConfigureAwait(false);
                LogPurged(logger, deleted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Progress cleanup: {Count} inactive students deleted")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress cleanup failed")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
