namespace Hechao.Contracts;

public enum ServerStatus
{
    Online,
    Maintenance,
    Closed
}

public enum ModLoaderKind
{
    Vanilla,
    Paper,
    NeoForge,
    Fabric,
    Forge
}

public enum AccessTier
{
    Member,
    Participant,
    Collaborator,
    Administrator
}

public enum ServerCatalogSection
{
    Permanent,
    Activity
}

public sealed record ServerSummary(
    string Id,
    string Name,
    string ShortName,
    string IconGlyph,
    ServerStatus Status,
    int OnlinePlayers,
    int MaxPlayers,
    string MinecraftVersion,
    ModLoaderKind Loader,
    AccessTier MinimumTier,
    string ClientProfileId,
    string Announcement = "",
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    ServerCatalogSection? CatalogSection = null);

public sealed record ClientProfileSummary(
    string Id,
    string DisplayName,
    string Version,
    long DownloadBytes,
    string Sha256,
    DateTimeOffset PublishedAt);

public sealed record LauncherCatalogSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ServerSummary> Servers,
    IReadOnlyList<ClientProfileSummary> ClientProfiles);

public sealed record PublicActivitySummary(
    string Id,
    string Name,
    ServerStatus Status,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int MaxPlayers,
    string MinecraftVersion,
    ModLoaderKind Loader,
    AccessTier MinimumTier);

public sealed record PublicActivityCatalogSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PublicActivitySummary> Activities);

public sealed record AuthenticatedPlayer(
    Guid UserId,
    Guid MinecraftUuid,
    string MinecraftName,
    string LuckPermsPrimaryGroup,
    AccessTier AccessTier,
    DateTimeOffset? LuckPermsSyncedAt);

public sealed record HechaoAccount(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    Guid? MinecraftUuid,
    string? MinecraftName,
    string LuckPermsPrimaryGroup,
    AccessTier AccessTier,
    DateTimeOffset? LuckPermsSyncedAt,
    DateTimeOffset CreatedAt)
{
    public bool IsMinecraftLinked =>
        MinecraftUuid is not null && !string.IsNullOrWhiteSpace(MinecraftName);
}

public sealed record HechaoAccountRegisterRequest(
    string Username,
    string DisplayName,
    string Password,
    string? Email);

public sealed record HechaoAccountLoginRequest(
    string UsernameOrEmail,
    string Password);

public sealed record MinecraftSessionExchangeRequest(string MinecraftAccessToken);
public sealed record MinecraftIdentityLinkRequest(string MinecraftAccessToken);
public sealed record MinecraftIdentityUnlinkRequest(string CurrentPassword);

public sealed record RefreshSessionRequest(string RefreshToken);

public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    HechaoAccount Account);

public sealed record SessionRevocationResponse(
    int RevokedLauncherSessions,
    int RevokedAdminSessions);

public sealed record LuckPermsPlayerSnapshot(
    Guid MinecraftUuid,
    string MinecraftName,
    string PrimaryGroup);

public sealed record LuckPermsSnapshotRequest(
    DateTimeOffset CapturedAt,
    bool IsFullSnapshot,
    IReadOnlyList<LuckPermsPlayerSnapshot> Players);

public sealed record LuckPermsSnapshotResponse(
    int ImportedPlayers,
    int UpdatedIdentities,
    DateTimeOffset ReceivedAt);

public enum ServerMetricIssueCode
{
    StatusTimeout,
    StatusUnavailable,
    ProcessProbeNotConfigured,
    ProcessNotRunning,
    ProcessAccessDenied,
    ProcessProbeFailed,
    DiskProbeFailed,
    MetricsNotConfigured,
    MetricsFileMissing,
    MetricsFileStale,
    MetricsFileInvalid
}

public sealed record VelocityTargetHeartbeat(
    string VelocityTarget,
    bool Online,
    int OnlinePlayers,
    int MaxPlayers,
    string? SoftwareVersion,
    int? ProtocolVersion,
    long? ProcessWorkingSetBytes = null,
    long? ProcessPrivateBytes = null,
    double? ProcessCpuPercent = null,
    DateTimeOffset? ProcessStartedAt = null,
    long? DiskFreeBytes = null,
    long? DiskTotalBytes = null,
    double? Tps1m = null,
    double? Tps5m = null,
    double? Tps15m = null,
    double? MsptAverage = null,
    long? GcCollectionTimeMilliseconds = null,
    DateTimeOffset? MetricsCapturedAt = null,
    IReadOnlyList<ServerMetricIssueCode>? Issues = null);

public sealed record ServerHeartbeatBatchRequest(
    DateTimeOffset CapturedAt,
    string CollectorInstance,
    IReadOnlyList<VelocityTargetHeartbeat> Servers);

public sealed record ServerHeartbeatBatchResponse(
    int ImportedServers,
    DateTimeOffset ReceivedAt);

public sealed record AdminServerRuntimeBinding(
    string ServerId,
    string DisplayName,
    bool IsVisible);

public sealed record AdminServerRuntimeRecord(
    string VelocityTarget,
    IReadOnlyList<AdminServerRuntimeBinding> Servers,
    bool HasHeartbeat,
    bool IsFresh,
    bool Online,
    int OnlinePlayers,
    int MaxPlayers,
    string? SoftwareVersion,
    int? ProtocolVersion,
    long? ProcessWorkingSetBytes,
    long? ProcessPrivateBytes,
    double? ProcessCpuPercent,
    DateTimeOffset? ProcessStartedAt,
    long? DiskFreeBytes,
    long? DiskTotalBytes,
    double? Tps1m,
    double? Tps5m,
    double? Tps15m,
    double? MsptAverage,
    long? GcCollectionTimeMilliseconds,
    DateTimeOffset? MetricsCapturedAt,
    IReadOnlyList<ServerMetricIssueCode> Issues,
    string? CollectorInstance,
    DateTimeOffset? CapturedAt,
    DateTimeOffset? ReceivedAt);

public sealed record AdminServerRuntimeIssueSummary(
    ServerMetricIssueCode Issue,
    long Samples,
    int Targets);

public sealed record AdminServerRuntimeSummary(
    DateTimeOffset GeneratedAt,
    int FreshnessSeconds,
    IReadOnlyList<AdminServerRuntimeRecord> Targets,
    IReadOnlyList<AdminServerRuntimeIssueSummary> Issues);

public enum VelocityAuthorizationReason
{
    Allowed,
    PlayerNotLinked,
    PlayerDisabled,
    MinecraftIdentityBanned,
    ServerUnknown,
    ServerUnavailable,
    AccessDenied,
    InsufficientTier,
    PermissionDataStale,
    LaunchGrantRequired,
    LaunchGrantIpMismatch,
    MinecraftVersionMismatch,
    ClientProfileMismatch
}

public sealed record VelocityLaunchGrantRequest(string ServerId);

public sealed record VelocityLaunchGrantResponse(
    Guid GrantId,
    string ServerId,
    DateTimeOffset ExpiresAt);

public sealed record VelocityAuthorizationRequest(
    Guid MinecraftUuid,
    string MinecraftName,
    string VelocityTarget,
    bool InitialConnection,
    string? RemoteAddress,
    string ProxyInstance,
    string? SessionServerId = null);

public sealed record VelocityAuthorizationResponse(
    bool Allowed,
    VelocityAuthorizationReason Reason,
    string Message,
    string? ServerId,
    string VelocityTarget,
    AccessTier? AccessTier,
    string? LuckPermsPrimaryGroup,
    DateTimeOffset EvaluatedAt);

public enum CatalogSource
{
    Live,
    Cache,
    BuiltIn
}

public sealed record ServerCatalogResult(
    LauncherCatalogSnapshot Snapshot,
    CatalogSource Source);

public interface IServerCatalogClient
{
    Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);

    async Task<ServerCatalogResult> GetCatalogResultAsync(
        CancellationToken cancellationToken = default) =>
        new(await GetCatalogAsync(cancellationToken), CatalogSource.Live);
}
