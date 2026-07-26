using Microsoft.Extensions.Options;

namespace Hechao.Api.Telemetry;

public sealed class LauncherTelemetryCleanupService(
    LauncherTelemetryRepository repository,
    IOptions<LauncherTelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<LauncherTelemetryCleanupService> logger) : BackgroundService
{
    private readonly LauncherTelemetryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(_options.CleanupHours),
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
            var removed = await repository.DeleteBeforeAsync(
                timeProvider.GetUtcNow().AddDays(-_options.RetentionDays),
                cancellationToken);
            if (removed > 0)
            {
                logger.LogInformation(
                    "Removed {Count} expired launcher telemetry events.",
                    removed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Launcher telemetry cleanup failed.");
        }
    }
}
