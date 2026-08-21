using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hechao.Api.Economy;

public static partial class EconomyRules
{
    public static bool IsValidIdempotencyKey(string? value) =>
        value is not null && IdempotencyKeyRegex().IsMatch(value);

    public static bool IsValidMinecraftItemId(string? value) =>
        value is not null && MinecraftItemIdRegex().IsMatch(value);

    public static bool IsValidActorName(string? value) =>
        value is not null && value.Trim().Length is >= 1 and <= 64 &&
        !value.Any(char.IsControl);

    public static bool IsValidTransfer(
        EconomyTransferRequest? request,
        decimal maximumAmount) =>
        request is not null &&
        IsValidIdempotencyKey(request.IdempotencyKey) &&
        request.SenderUuid != Guid.Empty &&
        request.RecipientUuid != Guid.Empty &&
        request.SenderUuid != request.RecipientUuid &&
        IsCurrencyAmount(request.Amount) &&
        request.Amount <= maximumAmount &&
        (request.Note is null ||
         (request.Note.Trim().Length <= 120 && !request.Note.Any(char.IsControl)));

    public static bool IsValidQuote(EconomySaleQuoteRequest? request) =>
        request is not null &&
        request.PlayerUuid != Guid.Empty &&
        IsValidMinecraftItemId(request.ItemId) &&
        request.Quantity is >= 1 and <= 2304;

    public static bool IsValidCommit(EconomySaleCommitRequest? request) =>
        request is not null &&
        request.PlayerUuid != Guid.Empty &&
        request.QuoteId != Guid.Empty &&
        IsValidIdempotencyKey(request.IdempotencyKey);

    public static int CalculateSaleQuantity(
        int requestedQuantity,
        int personalUsed,
        int personalLimit,
        int serverUsed,
        int serverLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requestedQuantity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(personalUsed);
        ArgumentOutOfRangeException.ThrowIfNegative(personalLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(serverUsed);
        ArgumentOutOfRangeException.ThrowIfNegative(serverLimit);

        var personalRemaining = Math.Max(0, personalLimit - personalUsed);
        var serverRemaining = Math.Max(0, serverLimit - serverUsed);
        return Math.Min(
            requestedQuantity,
            Math.Min(personalRemaining, serverRemaining));
    }

    public static bool IsValidProductMutation(EconomyProductUpsertRequest? request) =>
        request is not null &&
        request.ActorUuid != Guid.Empty &&
        IsValidActorName(request.ActorName) &&
        IsCurrencyAmount(request.UnitPrice) &&
        request.UnitPrice > 0 &&
        request.PersonalDailyLimit is >= 1 and <= 1_000_000 &&
        request.ServerDailyLimit >= request.PersonalDailyLimit &&
        request.ServerDailyLimit <= 100_000_000;

    public static bool IsValidProductDisable(EconomyProductDisableRequest? request) =>
        request is not null &&
        request.ActorUuid != Guid.Empty &&
        IsValidActorName(request.ActorName);

    public static bool IsValidMarketListing(
        EconomyMarketCreateListingRequest? request,
        decimal maximumPrice) =>
        request is not null &&
        IsValidIdempotencyKey(request.IdempotencyKey) &&
        request.SellerUuid != Guid.Empty &&
        IsValidActorName(request.SellerName) &&
        IsValidMinecraftItemId(request.ItemId) &&
        request.Quantity is >= 1 and <= 2304 &&
        IsCurrencyAmount(request.TotalPrice) &&
        request.TotalPrice >= 1m &&
        request.TotalPrice <= maximumPrice;

    public static bool IsValidMarketPurchase(EconomyMarketPurchaseRequest? request) =>
        request is not null &&
        IsValidIdempotencyKey(request.IdempotencyKey) &&
        request.ListingId != Guid.Empty &&
        request.BuyerUuid != Guid.Empty &&
        IsValidActorName(request.BuyerName);

    public static bool IsValidMarketCancel(EconomyMarketCancelRequest? request) =>
        request is not null &&
        IsValidIdempotencyKey(request.IdempotencyKey) &&
        request.ListingId != Guid.Empty &&
        request.SellerUuid != Guid.Empty;

    public static bool IsValidMarketClaim(EconomyMarketClaimRequest? request) =>
        request is not null &&
        IsValidIdempotencyKey(request.IdempotencyKey) &&
        request.DeliveryId != Guid.Empty &&
        request.PlayerUuid != Guid.Empty;

    public static bool IsValidMarketQuery(string? query) =>
        query is null || (query.Trim().Length <= 80 && !query.Any(char.IsControl));

    public static bool TryParseMarketSort(
        string? value,
        out EconomyMarketSort sort)
    {
        sort = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "recently_listed" => EconomyMarketSort.RecentlyListed,
            "lowest_unit_price" => EconomyMarketSort.LowestUnitPrice,
            "highest_unit_price" => EconomyMarketSort.HighestUnitPrice,
            "expiring_soon" => EconomyMarketSort.ExpiringSoon,
            _ => default
        };

        return value is null
            || value.Trim().Length == 0
            || value.Trim().Equals("recently_listed", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("lowest_unit_price", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("highest_unit_price", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("expiring_soon", StringComparison.OrdinalIgnoreCase);
    }

    public static string MarketSortValue(EconomyMarketSort sort) => sort switch
    {
        EconomyMarketSort.LowestUnitPrice => "lowest_unit_price",
        EconomyMarketSort.HighestUnitPrice => "highest_unit_price",
        EconomyMarketSort.ExpiringSoon => "expiring_soon",
        _ => "recently_listed"
    };

    public static string Fingerprint(params object?[] values)
    {
        var canonical = string.Join('\n', values.Select(value => value switch
        {
            decimal amount => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            null => string.Empty,
            _ => value.ToString()?.Trim() ?? string.Empty
        }));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static bool IsCurrencyAmount(decimal amount) =>
        amount > 0 && decimal.Round(amount, 2) == amount;

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyRegex();

    [GeneratedRegex(
        "^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MinecraftItemIdRegex();
}
