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
    bool IsVisible);

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
