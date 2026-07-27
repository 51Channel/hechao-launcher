using Microsoft.Extensions.Options;

namespace Hechao.Api.Monitoring;

public sealed class ServerRuntimeSampleCleanupService(
    ServerRuntimeStatusRepository repository,
    IOptions<ServerHeartbeatOptions> options,
    TimeProvider timeProvider,
    ILogger<ServerRuntimeSampleCleanupService> logger) : BackgroundService
{
    private readonly ServerHeartbeatOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(_options.RuntimeHistoryCleanupHours),
            timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var removed = await repository.DeleteSamplesBeforeAsync(
                timeProvider.GetUtcNow()
                    .AddDays(-_options.RuntimeHistoryRetentionDays),
                cancellationToken);
            if (removed > 0)
            {
                logger.LogInformation(
                    "Removed {Count} expired server runtime samples.",
                    removed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Server runtime sample cleanup failed.");
        }
    }
}
