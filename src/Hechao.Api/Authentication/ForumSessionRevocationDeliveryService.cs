using Microsoft.Extensions.Options;

namespace Hechao.Api.Authentication;

public sealed class ForumSessionRevocationDeliveryService(
    ForumSessionRevocationRepository repository,
    ForumSessionRevocationClient client,
    IOptions<ForumSessionRevocationOptions> options,
    TimeProvider timeProvider,
    ILogger<ForumSessionRevocationDeliveryService> logger) : BackgroundService
{
    private readonly ForumSessionRevocationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.DeliveryIntervalSeconds),
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
            while (!cancellationToken.IsCancellationRequested)
            {
                var deliveries = await repository.ClaimDueAsync(
                    timeProvider.GetUtcNow(),
                    TimeSpan.FromSeconds(_options.LeaseSeconds),
                    _options.BatchSize,
                    cancellationToken);
                if (deliveries.Count == 0)
                {
                    return;
                }

                foreach (var delivery in deliveries)
                {
                    await DeliverOneAsync(delivery, cancellationToken);
                }

                if (deliveries.Count < _options.BatchSize)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Forum session revocation delivery cycle failed.");
        }
    }

    private async Task DeliverOneAsync(
        ForumSessionRevocationDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeliverAsync(delivery, cancellationToken);
            await repository.MarkCompletedAsync(
                delivery.RequestId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            logger.LogInformation(
                "Delivered forum session revocation {RequestId} for user {UserId}.",
                delivery.RequestId,
                delivery.UserId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var retryAt = timeProvider.GetUtcNow().Add(
                CalculateRetryDelay(delivery.AttemptCount));
            await repository.MarkFailedAsync(
                delivery.RequestId,
                retryAt,
                DescribeFailure(exception),
                cancellationToken);
            logger.LogWarning(
                "Forum session revocation {RequestId} delivery attempt {AttemptCount} failed; retry at {RetryAt}.",
                delivery.RequestId,
                delivery.AttemptCount,
                retryAt);
        }
    }

    internal static TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 6);
        return TimeSpan.FromSeconds(Math.Min(300, 5 * (1 << exponent)));
    }

    internal static string DescribeFailure(Exception exception) =>
        exception switch
        {
            HttpRequestException http when http.StatusCode is not null =>
                $"HTTP {(int)http.StatusCode.Value}",
            TaskCanceledException => "request timeout",
            _ => exception.GetType().Name
        };
}
