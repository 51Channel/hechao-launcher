namespace Hechao.Contracts;

public enum PackageImportStatus
{
    Uploading,
    Uploaded,
    Analyzing,
    AwaitingReview,
    QueuedForPublishing,
    PublishingClient,
    QueuedForDeployment,
    DeployingServer,
    Finalizing,
    Completed,
    Failed,
    Cancelled
}

public enum PackageImportIssueSeverity
{
    Information,
    Warning,
    Blocking
}

public sealed record PackageImportIssueRecord(
    string Code,
    PackageImportIssueSeverity Severity,
    string Message,
    string? Path);

public sealed record PackageImportDetectedMetadataRecord(
    string SuggestedProfileId,
    string DisplayName,
    string Version,
    string MinecraftVersion,
    int JavaMajorVersion,
    string Loader,
    string LoaderVersion,
    int? MaximumPlayers,
    string? ServerLaunchPath);

public sealed record PackageImportPartRecord(
    string Sha256,
    long ArchiveBytes,
    long ExpandedBytes,
    int FileCount);

public sealed record PackageImportFileSampleRecord(
    string Path,
    string Side,
    long Size,
    string Sha256);

public sealed record PackageImportAnalysisRecord(
    string Layout,
    PackageImportDetectedMetadataRecord Metadata,
    PackageImportPartRecord? Client,
    PackageImportPartRecord? Server,
    int ClientFileCount,
    int ServerFileCount,
    int SharedFileCount,
    IReadOnlyList<PackageImportFileSampleRecord> FileSamples,
    IReadOnlyList<PackageImportIssueRecord> Issues)
{
    public bool HasBlockingIssues =>
        Issues.Any(issue => issue.Severity == PackageImportIssueSeverity.Blocking);
}

public sealed record PackageImportDeploymentPlanRecord(
    string ProfileId,
    string ProfileDisplayName,
    string Version,
    string TargetServerId,
    bool PreserveWorldData,
    bool SyncServerCatalog,
    string ServerDisplayName,
    AccessTier MinimumTier,
    int MaximumMemoryMiB);

public enum PackagePublisherProgressPhase
{
    DownloadingArchive,
    ExtractingArchive,
    BuildingDistribution,
    PublishingObjects,
    Finalizing
}

public sealed record PackagePublisherProgressRecord(
    PackagePublisherProgressPhase Phase,
    int CompletedObjects,
    int TotalObjects,
    long ProcessedBytes,
    long TotalBytes,
    DateTimeOffset SampledAt);

public sealed record AdminPackageImportRecord(
    Guid ImportId,
    string FileName,
    long ExpectedUploadBytes,
    long UploadedBytes,
    string? SourceSha256,
    PackageImportStatus Status,
    PackageImportAnalysisRecord? Analysis,
    PackageImportDeploymentPlanRecord? Plan,
    string? ManifestSha256,
    Guid? DeploymentOperationId,
    string? ErrorCode,
    string? ErrorMessage,
    Guid CreatedBy,
    string? CreatedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    long Revision,
    PackagePublisherProgressRecord? PublisherProgress = null);

public sealed record AdminPackageImportListResponse(
    IReadOnlyList<AdminPackageImportRecord> Imports,
    bool PublisherAgentConnected,
    DateTimeOffset? PublisherAgentLastSeenAt);

public sealed record AdminPackageUploadCreateRequest(
    string FileName,
    long TotalBytes);

public sealed record AdminPackageUploadAppendResponse(
    Guid ImportId,
    long UploadedBytes,
    long ExpectedUploadBytes,
    bool Complete);

public sealed record AdminPackageImportConfirmRequest(
    long ExpectedRevision,
    string ProfileId,
    string ProfileDisplayName,
    string Version,
    string TargetServerId,
    bool PreserveWorldData,
    bool SyncServerCatalog,
    string ServerDisplayName,
    AccessTier MinimumTier,
    int MaximumMemoryMiB,
    string Confirmation);

public sealed record AdminPackageImportCancelRequest(
    long ExpectedRevision,
    string Reason);

public sealed record PackagePublisherHeartbeatRequest(
    string AgentId,
    string AgentVersion,
    DateTimeOffset CapturedAt,
    Guid? ActiveImportId = null);

public sealed record PackagePublisherHeartbeatResponse(
    DateTimeOffset ReceivedAt);

public sealed record PackagePublisherClaimRequest(
    string AgentId);

public sealed record PackagePublisherJobDelivery(
    Guid ImportId,
    int AttemptCount,
    string ProfileId,
    string Version,
    string MinecraftVersion,
    int JavaMajorVersion,
    string Loader,
    string LoaderVersion,
    long ClientArchiveBytes,
    string ClientArchiveSha256);

public sealed record PackagePublisherClaimResponse(
    PackagePublisherJobDelivery? Job,
    DateTimeOffset ClaimedAt);

public sealed record PackagePublisherProgressRequest(
    string AgentId,
    int AttemptCount,
    PackagePublisherProgressPhase Phase,
    int CompletedObjects,
    int TotalObjects,
    long ProcessedBytes,
    long TotalBytes);

public enum PackagePublisherJobOutcome
{
    Succeeded,
    Failed
}

public sealed record PackagePublisherCompletionRequest(
    string AgentId,
    int AttemptCount,
    PackagePublisherJobOutcome Outcome,
    string ResultCode,
    string ResultMessage,
    string? ManifestEnvelopeBase64,
    int UploadedObjects,
    int ExistingObjects,
    long UploadedBytes);
