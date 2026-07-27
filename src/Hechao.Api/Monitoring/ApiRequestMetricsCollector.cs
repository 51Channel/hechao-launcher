namespace Hechao.Api.Monitoring;

public enum ApiRequestMetricCategory
{
    All,
    Login,
    ObjectDownload
}

public sealed record ApiRequestMetricSnapshot(
    DateTimeOffset BucketStart,
    ApiRequestMetricCategory Category,
    long RequestCount,
    long ClientErrorCount,
    long ServerErrorCount,
    long TotalDurationMilliseconds,
    int MaximumDurationMilliseconds);

public sealed class ApiRequestMetricsCollector
{
    private readonly object _gate = new();
    private readonly Dictionary<MetricKey, MutableMetric> _metrics = [];

    public void Record(
        DateTimeOffset observedAt,
        string path,
        int statusCode,
        TimeSpan duration)
    {
        if (!path.StartsWith("/v1", StringComparison.Ordinal))
        {
            return;
        }

        var bucket = new DateTimeOffset(
            observedAt.Year,
            observedAt.Month,
            observedAt.Day,
            observedAt.Hour,
            observedAt.Minute,
            0,
            TimeSpan.Zero);
        var durationMilliseconds = (int)Math.Clamp(
            Math.Ceiling(duration.TotalMilliseconds),
            0,
            int.MaxValue);
        lock (_gate)
        {
            Add(
                new MetricKey(bucket, ApiRequestMetricCategory.All),
                statusCode,
                durationMilliseconds);
            if (string.Equals(path, "/v1/auth/login", StringComparison.Ordinal))
            {
                Add(
                    new MetricKey(bucket, ApiRequestMetricCategory.Login),
                    statusCode,
                    durationMilliseconds);
            }
            else if (path.StartsWith(
                         "/v1/profiles/",
                         StringComparison.Ordinal) &&
                     path.Contains("/objects/", StringComparison.Ordinal))
            {
                Add(
                    new MetricKey(bucket, ApiRequestMetricCategory.ObjectDownload),
                    statusCode,
                    durationMilliseconds);
            }
        }
    }

    public IReadOnlyList<ApiRequestMetricSnapshot> DrainCompleted(
        DateTimeOffset now) =>
        Drain(before: new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            0,
            TimeSpan.Zero));

    public IReadOnlyList<ApiRequestMetricSnapshot> DrainAll() =>
        Drain(before: null);

    private IReadOnlyList<ApiRequestMetricSnapshot> Drain(
        DateTimeOffset? before)
    {
        lock (_gate)
        {
            var keys = _metrics.Keys
                .Where(key => before is null || key.BucketStart < before.Value)
                .ToArray();
            var result = new List<ApiRequestMetricSnapshot>(keys.Length);
            foreach (var key in keys)
            {
                var metric = _metrics[key];
                _metrics.Remove(key);
                result.Add(new ApiRequestMetricSnapshot(
                    key.BucketStart,
                    key.Category,
                    metric.RequestCount,
                    metric.ClientErrorCount,
                    metric.ServerErrorCount,
                    metric.TotalDurationMilliseconds,
                    metric.MaximumDurationMilliseconds));
            }

            return result;
        }
    }

    private void Add(
        MetricKey key,
        int statusCode,
        int durationMilliseconds)
    {
        if (!_metrics.TryGetValue(key, out var metric))
        {
            metric = new MutableMetric();
            _metrics.Add(key, metric);
        }

        metric.RequestCount++;
        if (statusCode is >= 400 and < 500)
        {
            metric.ClientErrorCount++;
        }
        else if (statusCode >= 500)
        {
            metric.ServerErrorCount++;
        }

        metric.TotalDurationMilliseconds += durationMilliseconds;
        metric.MaximumDurationMilliseconds = Math.Max(
            metric.MaximumDurationMilliseconds,
            durationMilliseconds);
    }

    private readonly record struct MetricKey(
        DateTimeOffset BucketStart,
        ApiRequestMetricCategory Category);

    private sealed class MutableMetric
    {
        public long RequestCount { get; set; }
        public long ClientErrorCount { get; set; }
        public long ServerErrorCount { get; set; }
        public long TotalDurationMilliseconds { get; set; }
        public int MaximumDurationMilliseconds { get; set; }
    }
}
