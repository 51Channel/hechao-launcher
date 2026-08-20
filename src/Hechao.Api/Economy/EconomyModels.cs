namespace Hechao.Api.Economy;

public sealed record EconomyBalanceResponse(
    Guid PlayerUuid,
    decimal AvailableBalance,
    decimal FrozenBalance,
    DateTimeOffset? UpdatedAt);

public sealed record EconomyTransferRequest(
    string IdempotencyKey,
    Guid SenderUuid,
    Guid RecipientUuid,
    decimal Amount,
    string? Note);

public sealed record EconomyTransferResponse(
    Guid OperationId,
    string Status,
    Guid SenderUuid,
    Guid RecipientUuid,
    decimal Amount,
    decimal SenderBalance,
    decimal RecipientBalance,
    string? FailureCode = null);

public sealed record EconomySaleQuoteRequest(
    Guid PlayerUuid,
    string ItemId,
    int Quantity);

public sealed record EconomySaleQuoteResponse(
    Guid QuoteId,
    Guid PlayerUuid,
    string ItemId,
    int Quantity,
    decimal UnitPrice,
    decimal TotalAmount,
    int PersonalRemaining,
    int ServerRemaining,
    DateTimeOffset ExpiresAt);

public sealed record EconomySaleCommitRequest(
    string IdempotencyKey,
    Guid QuoteId,
    Guid PlayerUuid);

public sealed record EconomySaleCommitResponse(
    Guid OperationId,
    string Status,
    Guid QuoteId,
    Guid PlayerUuid,
    string ItemId,
    int Quantity,
    decimal Amount,
    decimal Balance,
    string? FailureCode = null);

public sealed record EconomyProductUpsertRequest(
    decimal UnitPrice,
    int PersonalDailyLimit,
    int ServerDailyLimit,
    Guid ActorUuid,
    string ActorName);

public sealed record EconomyProductDisableRequest(
    Guid ActorUuid,
    string ActorName);

public sealed record EconomyProductResponse(
    string ItemId,
    decimal UnitPrice,
    int PersonalDailyLimit,
    int ServerDailyLimit,
    bool Enabled,
    Guid UpdatedByUuid,
    string UpdatedByName,
    DateTimeOffset UpdatedAt);

public enum EconomyMarketSort
{
    RecentlyListed,
    LowestUnitPrice,
    HighestUnitPrice,
    ExpiringSoon
}

public enum EconomyQuoteStatus
{
    Created,
    ProductNotFound,
    ProductDisabled,
    PersonalLimitExceeded,
    ServerLimitExceeded
}

public sealed record EconomyQuoteResult(
    EconomyQuoteStatus Status,
    EconomySaleQuoteResponse? Quote = null);

public enum EconomyProductMutationStatus
{
    Applied,
    NotFound
}

public sealed record EconomyMarketListingResponse(
    Guid ListingId,
    string ServerId,
    Guid SellerUuid,
    string SellerName,
    string ItemId,
    int Quantity,
    decimal TotalPrice,
    decimal ListingFee,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public decimal UnitPrice => Quantity <= 0
        ? 0m
        : decimal.Round(
            TotalPrice / Quantity,
            4,
            MidpointRounding.AwayFromZero);
}

public sealed record EconomyMarketCreateListingRequest(
    string IdempotencyKey,
    Guid SellerUuid,
    string SellerName,
    string ItemId,
    int Quantity,
    decimal TotalPrice);

public sealed record EconomyMarketCreateListingResponse(
    Guid OperationId,
    string Status,
    EconomyMarketListingResponse? Listing,
    decimal ListingFee,
    decimal Balance,
    string? FailureCode = null);

public sealed record EconomyMarketPurchaseRequest(
    string IdempotencyKey,
    Guid ListingId,
    Guid BuyerUuid,
    string BuyerName);

public sealed record EconomyMarketPurchaseResponse(
    Guid OperationId,
    string Status,
    Guid ListingId,
    Guid? DeliveryId,
    string ItemId,
    int Quantity,
    decimal TotalPrice,
    decimal SellerProceeds,
    decimal TransactionTax,
    decimal BuyerBalance,
    string? FailureCode = null);

public sealed record EconomyMarketCancelRequest(
    string IdempotencyKey,
    Guid ListingId,
    Guid SellerUuid);

public sealed record EconomyMarketCancelResponse(
    Guid OperationId,
    string Status,
    Guid ListingId,
    Guid? DeliveryId,
    string ItemId,
    int Quantity,
    string? FailureCode = null);

public sealed record EconomyMarketDeliveryResponse(
    Guid DeliveryId,
    Guid PlayerUuid,
    Guid ListingId,
    string ServerId,
    string ItemId,
    int Quantity,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record EconomyMarketClaimRequest(
    string IdempotencyKey,
    Guid DeliveryId,
    Guid PlayerUuid);

public sealed record EconomyMarketClaimResponse(
    Guid OperationId,
    string Status,
    Guid DeliveryId,
    string ItemId,
    int Quantity,
    string? FailureCode = null);
