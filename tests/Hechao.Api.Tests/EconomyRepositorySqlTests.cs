using Hechao.Api.Economy;
using Xunit;

namespace Hechao.Api.Tests;

public sealed class EconomyRepositorySqlTests
{
    [Fact]
    public void ProductListSql_FiltersEnabledProductsWithoutJoiningSqlTokens()
    {
        var sql = EconomyRepository.BuildProductListSql(includeDisabled: false);

        Assert.Matches(
            @"FROM launcher\.economy_products\s+WHERE enabled\s+ORDER BY item_id;",
            sql);
        Assert.DoesNotContain("economy_productsWHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductListSql_CanIncludeDisabledProducts()
    {
        var sql = EconomyRepository.BuildProductListSql(includeDisabled: true);

        Assert.Matches(
            @"FROM launcher\.economy_products\s+ORDER BY item_id;",
            sql);
        Assert.DoesNotContain("WHERE enabled", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketListingSql_IsolatedByServerAndSearchesItemOrSeller()
    {
        var sql = EconomyRepository.BuildMarketListingSql();

        Assert.Contains("server_id = $1", sql, StringComparison.Ordinal);
        Assert.Contains("status = 'Active'", sql, StringComparison.Ordinal);
        Assert.Contains("item_id || ' ' || seller_name", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT $4", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketListingSql_UsesOnlyKnownStableSortExpressions()
    {
        var lowest = EconomyRepository.BuildMarketListingSql(
            EconomyMarketSort.LowestUnitPrice);
        var highest = EconomyRepository.BuildMarketListingSql(
            EconomyMarketSort.HighestUnitPrice);
        var expiring = EconomyRepository.BuildMarketListingSql(
            EconomyMarketSort.ExpiringSoon);

        Assert.Contains(
            "ORDER BY total_price / quantity ASC, created_at DESC, listing_id",
            lowest,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY total_price / quantity DESC, created_at DESC, listing_id",
            highest,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY expires_at ASC, created_at DESC, listing_id",
            expiring,
            StringComparison.Ordinal);
        Assert.DoesNotContain("$5", lowest, StringComparison.Ordinal);
    }
}
