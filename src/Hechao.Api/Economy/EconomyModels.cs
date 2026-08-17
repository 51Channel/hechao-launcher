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
