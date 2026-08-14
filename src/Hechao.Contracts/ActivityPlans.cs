namespace Hechao.Contracts;

public enum ActivityPlanStatus
{
    Draft,
    Published,
    Archived
}

public enum UnmanagedActivityScheduleIssue
{
    MissingPlanStatus,
    MissingOpensAt,
    MissingClosesAt,
    MissingPackageBinding
}

public sealed record AdminActivityPackageRecord(
    Guid ImportId,
    string ProfileId,
    string ProfileDisplayName,
    string Version,
    string ManifestSha256,
    string MinecraftVersion,
    ModLoaderKind Loader,
    string LoaderVersion,
    int MaximumPlayers,
    int MaximumMemoryMiB,
    bool PreserveWorldData,
    bool ProductionReady,
    bool ProfileArchived,
    DateTimeOffset CompletedAt);

public sealed record AdminActivityPlanRecord(
    string Id,
    string Title,
    string Announcement,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    int MaximumPlayers,
    AccessTier MinimumTier,
    Guid PackageImportId,
    string ProfileId,
    string ProfileDisplayName,
    string Version,
    string MinecraftVersion,
    ModLoaderKind Loader,
    ActivityPlanStatus Status,
    ServerStatus EffectiveStatus,
    bool ProductionReady,
    bool DeploymentMatches,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminActivitySlotRecord(
    bool Configured,
    bool AgentConnected,
    bool Online,
    bool ServerFilesPresent,
    ServerPackageDeploymentIdentity? DeployedPackage,
    AdminServerControlOperationRecord? ActiveOperation,
    ServerMemoryGuidance? MemoryGuidance);

public sealed record AdminUnmanagedActivityScheduleRecord(
    string Id,
    string Title,
    string Announcement,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    Guid? PackageImportId,
    string ClientProfileId,
    bool IsVisible,
    ServerStatus ConfiguredStatus,
    long Revision,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<UnmanagedActivityScheduleIssue> Issues);

public sealed record AdminActivityPlanListResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AdminActivityPlanRecord> Plans,
    IReadOnlyList<AdminActivityPackageRecord> Packages,
    AdminActivitySlotRecord Slot,
    IReadOnlyList<AdminUnmanagedActivityScheduleRecord> UnmanagedSchedules);

public sealed record AdminActivityPlanCreateRequest(
    string Title,
    string Announcement,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    int MaximumPlayers,
    AccessTier MinimumTier,
    Guid PackageImportId);

public sealed record AdminActivityPlanUpdateRequest(
    string Title,
    string Announcement,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    int MaximumPlayers,
    AccessTier MinimumTier,
    Guid PackageImportId,
    long ExpectedRevision);

public sealed record AdminActivityPlanRevisionRequest(long ExpectedRevision);

public sealed record AdminActivityPlanArchiveRequest(
    long ExpectedRevision,
    string Reason);

public sealed record AdminActivityPlanDeployRequest(
    long ExpectedRevision,
    string Confirmation,
    string Reason);
