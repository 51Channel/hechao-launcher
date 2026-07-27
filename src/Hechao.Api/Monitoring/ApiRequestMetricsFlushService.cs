namespace Hechao.Api.Monitoring;

public sealed class ApiRequestMetricsFlushService(
    ApiRequestMetricsCollector collector,
    OperationalAlertRepository repository,
    TimeProvider timeProvider,
    ILogger<ApiRequestMetricsFlushService> logger) : BackgroundService
{
    private readonly List<ApiRequestMetricSnapshot> _pending = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(15),
            timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(
                    collector.DrainCompleted(timeProvider.GetUtcNow()),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await FlushAsync(collector.DrainAll(), CancellationToken.None);
        }
    }

    private async Task FlushAsync(
        IReadOnlyList<ApiRequestMetricSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        _pending.AddRange(snapshots);
        if (_pending.Count == 0)
        {
            return;
        }

        try
        {
            await repository.UpsertRequestMetricsAsync(
                _pending,
                cancellationToken);
            _pending.Clear();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "API request metrics flush failed for {Count} buckets.",
                _pending.Count);
        }
    }
}
