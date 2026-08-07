using System.Text.Json;

namespace Hechao.Contracts;

public enum AdminServerRole
{
    Player,
    Infrastructure
}

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
    bool AllowsProtocolTranslation,
    AdminServerRole Role,
    bool MonitoringEnabled,
    int SortOrder,
    bool IsVisible,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    ServerStatus EffectiveStatus,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasControlTarget = false,
    bool ControlTargetFresh = false,
    bool? ControlReportedOnline = null,
    DateTimeOffset? ControlLastSeenAt = null);

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
    bool AllowsProtocolTranslation,
    int SortOrder,
    bool IsVisible,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    AdminServerRole Role = AdminServerRole.Player,
    bool MonitoringEnabled = true);

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
    bool AllowsProtocolTranslation,
    int SortOrder,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    long ExpectedRevision,
    AdminServerRole Role = AdminServerRole.Player,
    bool MonitoringEnabled = true);

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
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    string ArchiveReason,
    int ServerReferenceCount,
    bool CanDelete,
    DateTimeOffset UpdatedAt,
    long Revision,
    int ReleaseCount,
    IReadOnlyList<AdminClientProfileChannelRecord> Channels);

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
    bool IsMinecraftIdentityBanned,
    int ActiveRuleCount,
    DateTimeOffset CreatedAt);

public sealed record AdminDeviceSessionRecord(
    Guid SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset RefreshExpiresAt,
    string? SourceIp);

public sealed record AdminMinecraftIdentityBanRecord(
    Guid MinecraftUuid,
    string Reason,
    DateTimeOffset? ExpiresAt,
    Guid CreatedBy,
    string? CreatedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    Guid? RevokedBy,
    string? RevokedReason,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record AdminUserSecuritySummary(
    AdminUserSummary User,
    IReadOnlyList<AdminDeviceSessionRecord> LauncherSessions,
    int ActiveAdminSessions,
    int PendingAdminTickets,
    int PendingVelocityLaunchGrants,
    int PendingForumSessionRevocations,
    AdminLuckPermsTierChangeRecord? PendingLuckPermsTierChange,
    AdminMinecraftIdentityBanRecord? MinecraftIdentityBan);

public sealed record AdminSecurityReasonRequest(string Reason);

public sealed record AdminMinecraftIdentityBanRequest(
    string Reason,
    DateTimeOffset? ExpiresAt,
    long? ExpectedRevision);

public sealed record AdminMinecraftIdentityBanDeleteRequest(
    string Reason,
    long ExpectedRevision);

public sealed record AdminSecurityRevocationCounts(
    int LauncherSessions,
    int AdminSessions,
    int AdminTickets,
    int VelocityLaunchGrants,
    int ForumSessionRevocations);

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
    MinecraftIdentityBanned,
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
