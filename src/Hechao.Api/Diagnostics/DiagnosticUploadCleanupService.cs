using Microsoft.Extensions.Options;

namespace Hechao.Api.Diagnostics;

public sealed class DiagnosticUploadCleanupService(
    DiagnosticUploadRepository repository,
    DiagnosticUploadStorage storage,
    IOptions<DiagnosticUploadOptions> options,
    TimeProvider timeProvider,
    ILogger<DiagnosticUploadCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _interval =
        TimeSpan.FromMinutes(options.Value.CleanupMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(_interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var expired = await repository.ExpireAsync(cancellationToken);
            foreach (var uploadId in expired)
            {
                storage.Delete(uploadId);
            }

            var orphanedParts = storage.DeleteOrphanedTemporaryFiles(
                timeProvider.GetUtcNow().Subtract(_interval + _interval));
            if (expired.Count > 0 || orphanedParts > 0)
            {
                logger.LogInformation(
                    "Expired {ExpiredCount} diagnostic uploads and removed {PartCount} orphaned parts.",
                    expired.Count,
                    orphanedParts);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Diagnostic upload cleanup failed.");
        }
    }
}
