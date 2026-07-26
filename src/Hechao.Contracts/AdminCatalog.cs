using System.Text.Json;

namespace Hechao.Contracts;

public sealed record AdminServerRecord(
    string Id,
    string DisplayName,
    string ShortName,
    string IconGlyph,
    ServerStatus Status,
    int MaxPlayers,
    string MinecraftVersion,
    ModLoaderKind Loader,
    AccessTier MinimumTier,
    string ClientProfileId,
    string VelocityTarget,
    int SortOrder,
    bool IsVisible,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    ServerStatus EffectiveStatus,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminServerCreateRequest(
    string Id,
    string DisplayName,
    string ShortName,
    string IconGlyph,
    ServerStatus Status,
    int MaxPlayers,
    string MinecraftVersion,
    ModLoaderKind Loader,
    AccessTier MinimumTier,
    string ClientProfileId,
    string VelocityTarget,
    int SortOrder,
    bool IsVisible,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt);

public sealed record AdminServerUpdateRequest(
    string DisplayName,
    string ShortName,
    string IconGlyph,
    ServerStatus Status,
    int MaxPlayers,
    string MinecraftVersion,
    ModLoaderKind Loader,
    AccessTier MinimumTier,
    string ClientProfileId,
    string VelocityTarget,
    int SortOrder,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    long ExpectedRevision);

public sealed record AdminServerVisibilityRequest(
    bool IsVisible,
    long ExpectedRevision);

public sealed record AdminClientProfileRecord(
    string Id,
    string DisplayName,
    string Version,
    long DownloadBytes,
    string Sha256,
    DateTimeOffset PublishedAt,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed record AdminAuditLogEntry(
    long Id,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string TargetType,
    string TargetId,
    string? SourceIp,
    JsonElement? BeforeData,
    JsonElement? AfterData,
    DateTimeOffset CreatedAt);

public enum AdminServerAccessDecision
{
    Allow,
    Deny
}

public sealed record AdminUserSummary(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    Guid? MinecraftUuid,
    string? MinecraftName,
    string LuckPermsPrimaryGroup,
    AccessTier AccessTier,
    DateTimeOffset? LuckPermsSyncedAt,
    bool IsDisabled,
    int ActiveRuleCount,
    DateTimeOffset CreatedAt);

public sealed record AdminServerAccessRuleRecord(
    Guid UserId,
    string ServerId,
    AdminServerAccessDecision Decision,
    string Reason,
    DateTimeOffset? ExpiresAt,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminServerAccessRuleUpsertRequest(
    AdminServerAccessDecision Decision,
    string Reason,
    DateTimeOffset? ExpiresAt,
    long? ExpectedRevision);

public sealed record AdminServerAccessRuleDeleteRequest(long ExpectedRevision);

public enum AdminEffectiveAccessReason
{
    AllowedByTier,
    AllowedByRule,
    PlayerNotLinked,
    PlayerDisabled,
    ServerArchived,
    ServerUnavailable,
    DeniedByRule,
    InsufficientTier,
    PermissionDataStale
}

public sealed record AdminServerAccessPreviewRecord(
    string ServerId,
    string ServerDisplayName,
    ServerStatus ConfiguredStatus,
    ServerStatus EffectiveStatus,
    bool IsVisible,
    AccessTier MinimumTier,
    bool Allowed,
    AdminEffectiveAccessReason Reason,
    AdminServerAccessRuleRecord? Rule);

public sealed record AdminUserAccessPreview(
    AdminUserSummary User,
    IReadOnlyList<AdminServerAccessPreviewRecord> Servers);
