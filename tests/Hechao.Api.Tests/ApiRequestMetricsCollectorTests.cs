using Hechao.Api.Monitoring;

namespace Hechao.Api.Tests;

public sealed class ApiRequestMetricsCollectorTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-27T08:14:30Z");

    [Fact]
    public void DrainCompleted_AggregatesAllAndSensitiveCategories()
    {
        var collector = new ApiRequestMetricsCollector();
        collector.Record(
            Now,
            "/v1/auth/login",
            401,
            TimeSpan.FromMilliseconds(125));
        collector.Record(
            Now,
            "/v1/profiles/base/objects/aa/aabb",
            503,
            TimeSpan.FromMilliseconds(250));
        collector.Record(
            Now,
            "/healthz",
            200,
            TimeSpan.FromMilliseconds(10));

        var snapshots = collector.DrainCompleted(
            Now.AddMinutes(1));

        var all = Assert.Single(
            snapshots,
            item => item.Category == ApiRequestMetricCategory.All);
        var login = Assert.Single(
            snapshots,
            item => item.Category == ApiRequestMetricCategory.Login);
        var download = Assert.Single(
            snapshots,
            item => item.Category ==
                    ApiRequestMetricCategory.ObjectDownload);
        Assert.Equal(2, all.RequestCount);
        Assert.Equal(1, all.ClientErrorCount);
        Assert.Equal(1, all.ServerErrorCount);
        Assert.Equal(375, all.TotalDurationMilliseconds);
        Assert.Equal(250, all.MaximumDurationMilliseconds);
        Assert.Equal(1, login.ClientErrorCount);
        Assert.Equal(1, download.ServerErrorCount);
    }

    [Fact]
    public void DrainCompleted_PreservesCurrentMinute()
    {
        var collector = new ApiRequestMetricsCollector();
        collector.Record(
            Now,
            "/v1/catalog",
            200,
            TimeSpan.FromMilliseconds(12));

        Assert.Empty(collector.DrainCompleted(Now));
        Assert.Single(collector.DrainAll());
    }
}
