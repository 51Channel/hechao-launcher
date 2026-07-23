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
    string ClientProfileId);

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

public sealed record AuthenticatedPlayer(
    Guid UserId,
    Guid MinecraftUuid,
    string MinecraftName,
    string LuckPermsPrimaryGroup,
    AccessTier AccessTier,
    DateTimeOffset? LuckPermsSyncedAt);

public sealed record MinecraftSessionExchangeRequest(string MinecraftAccessToken);

public sealed record RefreshSessionRequest(string RefreshToken);

public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedPlayer Player);

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

public enum VelocityAuthorizationReason
{
    Allowed,
    PlayerNotLinked,
    PlayerDisabled,
    ServerUnknown,
    ServerUnavailable,
    AccessDenied,
    InsufficientTier,
    PermissionDataStale,
    LaunchGrantRequired,
    LaunchGrantIpMismatch
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
    string ProxyInstance);

public sealed record VelocityAuthorizationResponse(
    bool Allowed,
    VelocityAuthorizationReason Reason,
    string Message,
    string? ServerId,
    string VelocityTarget,
    AccessTier? AccessTier,
    string? LuckPermsPrimaryGroup,
    DateTimeOffset EvaluatedAt);

public interface IServerCatalogClient
{
    Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);
}
