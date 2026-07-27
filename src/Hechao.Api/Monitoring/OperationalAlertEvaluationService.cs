using Microsoft.Extensions.Options;

namespace Hechao.Api.Monitoring;

public sealed class OperationalAlertEvaluationService(
    OperationalAlertRepository repository,
    IOptions<OperationalAlertOptions> options,
    TimeProvider timeProvider,
    ILogger<OperationalAlertEvaluationService> logger) : BackgroundService
{
    private readonly OperationalAlertOptions _options = options.Value;
    private DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Operational alert evaluation is disabled.");
            return;
        }

        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.EvaluationSeconds),
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
            await repository.EvaluateAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (now - _lastCleanupAt >= TimeSpan.FromHours(6))
            {
                var removed = await repository.DeleteRequestMetricsBeforeAsync(
                    now.AddDays(-_options.RequestMetricsRetentionDays),
                    cancellationToken);
                _lastCleanupAt = now;
                if (removed > 0)
                {
                    logger.LogInformation(
                        "Removed {Count} expired API request metric buckets.",
                        removed);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operational alert evaluation failed.");
        }
    }
}
