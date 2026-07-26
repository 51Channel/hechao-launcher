namespace Hechao.Contracts;

public enum ClientProfileReleaseChannel
{
    Test,
    Gray,
    Production
}

public sealed record AdminClientProfileChannelRecord(
    ClientProfileReleaseChannel Channel,
    string? ManifestSha256,
    string? Version,
    int RolloutPercentage,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record AdminClientProfileReleaseRecord(
    string ProfileId,
    string ManifestSha256,
    string Version,
    long DownloadBytes,
    int FileCount,
    string MinecraftVersion,
    string JavaVersion,
    string Loader,
    string LoaderVersion,
    DateTimeOffset PublishedAt,
    bool IsPaused,
    string PauseReason,
    long Revision,
    DateTimeOffset CreatedAt,
    string? CreatedByDisplayName);

public sealed record AdminClientProfileDetail(
    AdminClientProfileRecord Profile,
    IReadOnlyList<AdminClientProfileReleaseRecord> Releases);

public sealed record AdminClientProfileCreateRequest(
    string Id,
    string DisplayName);

public sealed record AdminClientProfileUpdateRequest(
    string DisplayName,
    bool IsActive,
    long ExpectedRevision);

public sealed record AdminClientProfileChannelUpdateRequest(
    string? ManifestSha256,
    int RolloutPercentage,
    long ExpectedRevision);

public sealed record AdminClientProfileChannelRollbackRequest(
    long ExpectedRevision);

public sealed record AdminClientProfileReleasePauseRequest(
    bool IsPaused,
    string Reason,
    long ExpectedRevision);
