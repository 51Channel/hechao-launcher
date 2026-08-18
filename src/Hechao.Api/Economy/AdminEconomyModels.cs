namespace Hechao.Api.Economy;

public sealed record AdminEconomyOverview(
    DateTimeOffset From,
    DateTimeOffset To,
    int Hours,
    string? ServerId,
    IReadOnlyList<AdminEconomyServerOption> Servers,
    IReadOnlyList<AdminEconomyItemOption> Items,
    AdminEconomySummary Summary,
    AdminEconomyWealthSummary Wealth,
    IReadOnlyList<AdminEconomySeriesPoint> Series,
    IReadOnlyList<AdminEconomyPlayerBalance> TopBalances,
    IReadOnlyList<AdminEconomyProductVolume> Products,
    IReadOnlyList<AdminEconomyServerVolume> ServerVolumes);

public sealed record AdminEconomySummary(
    decimal TotalSupply,
    decimal WindowIssued,
    decimal TransferVolume,
    long ActivePlayers,
    long OperationCount);

public sealed record AdminEconomyWealthSummary(
    long FundedAccounts,
    decimal AverageBalance,
    decimal MedianBalance,
    decimal P90Balance,
    decimal TopTenPercentShare);

public sealed record AdminEconomySeriesPoint(
    DateTimeOffset At,
    decimal TotalSupply,
    decimal IssuedAmount);

public sealed record AdminEconomyPlayerBalance(
    Guid PlayerUuid,
    string? PlayerName,
    decimal Balance,
    decimal SupplyShare);

public sealed record AdminEconomyProductVolume(
    string ItemId,
    long Quantity,
    decimal Amount,
    long Sellers);

public sealed record AdminEconomyServerVolume(
    string ServerId,
    string DisplayName,
    decimal SaleVolume,
    decimal TransferVolume,
    long ActivePlayers,
    long OperationCount);

public sealed record AdminEconomyServerOption(
    string ServerId,
    string DisplayName);

public sealed record AdminEconomyItemOption(
    string ItemId,
    decimal? CurrentUnitPrice,
    bool Enabled);

public sealed record AdminEconomyItemHistory(
    DateTimeOffset From,
    DateTimeOffset To,
    int Hours,
    string? ServerId,
    string ItemId,
    decimal? CurrentUnitPrice,
    bool Enabled,
    AdminEconomyItemSummary Summary,
    IReadOnlyList<AdminEconomyItemSeriesPoint> Series);

public sealed record AdminEconomyItemSummary(
    decimal? OpenUnitPrice,
    decimal? CloseUnitPrice,
    decimal? LowUnitPrice,
    decimal? HighUnitPrice,
    decimal? PriceChangeRate,
    long Quantity,
    decimal Amount,
    long Sellers,
    long Transactions);

public sealed record AdminEconomyItemSeriesPoint(
    DateTimeOffset At,
    decimal? OpenUnitPrice,
    decimal? CloseUnitPrice,
    decimal? AverageUnitPrice,
    decimal? LowUnitPrice,
    decimal? HighUnitPrice,
    long Quantity,
    decimal Amount,
    long Sellers,
    long Transactions);

internal sealed record AdminEconomyWindow(
    int Hours,
    DateTimeOffset From,
    DateTimeOffset To,
    TimeSpan BucketSize,
    int BucketCount)
{
    private static readonly IReadOnlyDictionary<int, (TimeSpan BucketSize, int BucketCount)>
        Supported = new Dictionary<int, (TimeSpan, int)>
        {
            [24] = (TimeSpan.FromHours(1), 24),
            [168] = (TimeSpan.FromDays(1), 7),
            [720] = (TimeSpan.FromDays(1), 30),
            [2160] = (TimeSpan.FromDays(1), 90)
        };

    public static bool IsSupported(int hours) => Supported.ContainsKey(hours);

    public static AdminEconomyWindow Create(int hours, DateTimeOffset now)
    {
        var (bucketSize, bucketCount) = Supported[hours];
        var utc = now.ToUniversalTime();
        var currentBucket = bucketSize == TimeSpan.FromHours(1)
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return new AdminEconomyWindow(
            hours,
            currentBucket - TimeSpan.FromTicks(bucketSize.Ticks * (bucketCount - 1L)),
            utc,
            bucketSize,
            bucketCount);
    }

    public IEnumerable<DateTimeOffset> Buckets()
    {
        for (var index = 0; index < BucketCount; index += 1)
        {
            yield return From + TimeSpan.FromTicks(BucketSize.Ticks * index);
        }
    }
}
