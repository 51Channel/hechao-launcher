namespace Hechao.Contracts;

public enum LuckPermsTierCommandStatus
{
    Pending,
    Claimed,
    Applied,
    Conflict,
    Failed
}

public enum LuckPermsTierCommandOutcome
{
    Applied,
    Conflict,
    Failed
}

public sealed record AdminLuckPermsTierChangeRequest(
    AccessTier TargetTier,
    string ExpectedPrimaryGroup,
    string Reason);

public sealed record AdminLuckPermsTierChangeRecord(
    Guid CommandId,
    Guid UserId,
    Guid MinecraftUuid,
    string ExpectedPrimaryGroup,
    string TargetPrimaryGroup,
    AccessTier TargetAccessTier,
    string Reason,
    LuckPermsTierCommandStatus Status,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? ClaimExpiresAt,
    int AttemptCount,
    DateTimeOffset? CompletedAt,
    string? ObservedPrimaryGroup,
    string? FailureCode);

public sealed record LuckPermsTierCommandClaimRequest(
    string AgentId,
    string AgentVersion,
    int ProtocolVersion,
    int Limit);

public sealed record LuckPermsTierCommandDelivery(
    Guid CommandId,
    Guid MinecraftUuid,
    string ExpectedPrimaryGroup,
    string TargetPrimaryGroup,
    AccessTier TargetAccessTier,
    int AttemptCount);

public sealed record LuckPermsTierCommandClaimResponse(
    IReadOnlyList<LuckPermsTierCommandDelivery> Commands,
    DateTimeOffset ClaimedAt);

public sealed record LuckPermsTierCommandCompletionRequest(
    string AgentId,
    string AgentVersion,
    int ProtocolVersion,
    int AttemptCount,
    LuckPermsTierCommandOutcome Outcome,
    string ObservedPrimaryGroup,
    string? FailureCode);
