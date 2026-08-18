using Hechao.Api.Economy;
using Xunit;

namespace Hechao.Api.Tests;

public sealed class AdminEconomyTests
{
    [Theory]
    [InlineData(24, 24, 1)]
    [InlineData(168, 7, 24)]
    [InlineData(720, 30, 24)]
    [InlineData(2160, 90, 24)]
    public void Window_UsesSupportedBucketLayout(
        int hours,
        int expectedBuckets,
        int expectedBucketHours)
    {
        var now = new DateTimeOffset(2026, 8, 18, 12, 34, 56, TimeSpan.Zero);

        var window = AdminEconomyWindow.Create(hours, now);

        Assert.Equal(hours, window.Hours);
        Assert.Equal(expectedBuckets, window.BucketCount);
        Assert.Equal(TimeSpan.FromHours(expectedBucketHours), window.BucketSize);
        Assert.Equal(expectedBuckets, window.Buckets().Count());
        Assert.Equal(now, window.To);
    }

    [Fact]
    public void Window_RejectsUnsupportedDurations()
    {
        Assert.True(AdminEconomyWindow.IsSupported(24));
        Assert.True(AdminEconomyWindow.IsSupported(2160));
        Assert.False(AdminEconomyWindow.IsSupported(48));
    }

    [Fact]
    public void Queries_SeparateGlobalSupplyFromFilteredFlow()
    {
        var hourly = AdminEconomyRepository.BuildSeriesSql(TimeSpan.FromHours(1));
        var daily = AdminEconomyRepository.BuildSeriesSql(TimeSpan.FromDays(1));

        Assert.Contains("date_trunc('hour'", hourly, StringComparison.Ordinal);
        Assert.Contains("date_trunc('day'", daily, StringComparison.Ordinal);
        Assert.Contains("$3::text IS NULL OR o.server_id = $3", hourly, StringComparison.Ordinal);
        Assert.Contains("o.operation_kind = 'Sale'", hourly, StringComparison.Ordinal);
        Assert.Contains("o.status = 'Applied'", hourly, StringComparison.Ordinal);
        Assert.Contains("o.operation_kind = 'Transfer'", AdminEconomyRepository.WindowMetricsSql, StringComparison.Ordinal);
        Assert.Contains("le.amount > 0", AdminEconomyRepository.WindowMetricsSql, StringComparison.Ordinal);
        Assert.DoesNotContain("server_id = $3", AdminEconomyRepository.ServerVolumesSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemQueries_UseCommittedQuotesAndPreserveEmptyPriceBuckets()
    {
        var hourly = AdminEconomyRepository.BuildItemSeriesSql(TimeSpan.FromHours(1));
        var daily = AdminEconomyRepository.BuildItemSeriesSql(TimeSpan.FromDays(1));

        Assert.Contains("date_trunc('hour'", hourly, StringComparison.Ordinal);
        Assert.Contains("date_trunc('day'", daily, StringComparison.Ordinal);
        Assert.Contains("q.status = 'Committed'", hourly, StringComparison.Ordinal);
        Assert.Contains("o.status = 'Applied'", hourly, StringComparison.Ordinal);
        Assert.Contains("q.item_id = $4", hourly, StringComparison.Ordinal);
        Assert.Contains("array_agg(q.unit_price ORDER BY o.created_at, q.quote_id)", hourly, StringComparison.Ordinal);
        Assert.Contains("array_agg(q.unit_price ORDER BY o.created_at DESC, q.quote_id DESC)", hourly, StringComparison.Ordinal);
        Assert.Contains("sum(q.total_amount) / NULLIF(sum(q.quantity), 0)", hourly, StringComparison.Ordinal);
        Assert.Contains("count(DISTINCT q.player_uuid)", hourly, StringComparison.Ordinal);
        Assert.Contains("SELECT item_id FROM launcher.economy_products", AdminEconomyRepository.ItemOptionsSql, StringComparison.Ordinal);
        Assert.Contains("SELECT item_id FROM launcher.economy_sale_quotes", AdminEconomyRepository.ItemOptionsSql, StringComparison.Ordinal);
    }
}
