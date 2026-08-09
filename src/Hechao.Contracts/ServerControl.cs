namespace Hechao.Contracts;

public enum ServerControlAction
{
    Start,
    Stop,
    Restart,
    ConsoleCommand,
    ApplySettings,
    DeployPackage,
    DeleteServerFiles
}

public enum ServerControlOperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum ServerControlCommandKind
{
    Start,
    Stop,
    ConsoleCommand,
    ApplySettings,
    DeployPackage,
    DeleteServerFiles
}

public enum ServerControlCommandOutcome
{
    Succeeded,
    Failed,
    Conflict
}

public sealed record ServerQuickSettings(
    int MaxPlayers,
    int ViewDistance,
    int SimulationDistance,
    string Difficulty,
    bool WhiteList,
    int? InitialMemoryMiB = null,
    int? MaximumMemoryMiB = null,
    int? MaximumAllowedMemoryMiB = null);

public sealed record ServerMemoryGuidance(
    int HostTotalMemoryMiB,
    int RecommendedMinimumMemoryMiB,
    int RecommendedMaximumMemoryMiB);

public sealed record ServerPackageDeploymentIdentity(
    Guid ImportId,
    string ProfileId,
    string Version);

public sealed record AdminServerControlRequest(
    ServerControlAction Action,
    string Confirmation,
    string Reason,
    string? ConsoleCommand = null,
    ServerQuickSettings? Settings = null);

public sealed record AdminServerControlOperationRecord(
    Guid OperationId,
    string ServerId,
    string DisplayName,
    ServerControlAction Action,
    ServerControlOperationStatus Status,
    string Reason,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultCode,
    string? ResultMessage,
    IReadOnlyList<string> AutomaticallyStoppingServerIds);

public sealed record AdminServerControlTargetSummaryRecord(
    string ServerId,
    string DisplayName,
    string AgentId,
    string? ConflictGroup,
    int Port,
    bool AgentConnected,
    DateTimeOffset LastSeenAt,
    bool Online,
    int? ProcessId,
    ServerQuickSettings? Settings,
    AdminServerControlOperationRecord? ActiveOperation,
    bool PackageDeploymentEnabled = false,
    bool ServerDeletionEnabled = false,
    bool ServerFilesPresent = true,
    bool DeletionCleanupPending = false,
    ServerMemoryGuidance? PackageDeploymentMemoryGuidance = null,
    ServerPackageDeploymentIdentity? DeployedPackage = null);

public sealed record AdminServerControlTargetRecord(
    string ServerId,
    string DisplayName,
    string AgentId,
    string? ConflictGroup,
    int Port,
    bool AgentConnected,
    DateTimeOffset LastSeenAt,
    bool Online,
    int? ProcessId,
    ServerQuickSettings? Settings,
    IReadOnlyList<string> AllowedCommandPrefixes,
    string ConsoleTail,
    DateTimeOffset? ConsoleCapturedAt,
    AdminServerControlOperationRecord? ActiveOperation,
    bool PackageDeploymentEnabled = false,
    bool ServerDeletionEnabled = false,
    bool ServerFilesPresent = true,
    bool DeletionCleanupPending = false,
    ServerMemoryGuidance? PackageDeploymentMemoryGuidance = null,
    ServerPackageDeploymentIdentity? DeployedPackage = null);

public sealed record AdminServerControlOverview(
    DateTimeOffset GeneratedAt,
    int AgentFreshnessSeconds,
    IReadOnlyList<AdminServerControlTargetSummaryRecord> Targets);

public sealed record AdminServerControlTargetDetail(
    DateTimeOffset GeneratedAt,
    int AgentFreshnessSeconds,
    AdminServerControlTargetRecord Target,
    IReadOnlyList<AdminServerControlOperationRecord> RecentOperations);

public sealed record AdminServerControlQueueResult(
    AdminServerControlOperationRecord Operation,
    IReadOnlyList<string> AutomaticallyStoppingServerIds);

public sealed record ServerControlAgentTargetHeartbeat(
    string ServerId,
    string? ConflictGroup,
    int Port,
    bool Online,
    int? ProcessId,
    ServerQuickSettings? Settings,
    IReadOnlyList<string> AllowedCommandPrefixes,
    string ConsoleTail,
    DateTimeOffset? ConsoleCapturedAt,
    bool PackageDeploymentEnabled = false,
    bool ServerDeletionEnabled = false,
    bool ServerFilesPresent = true,
    bool DeletionCleanupPending = false,
    ServerPackageDeploymentIdentity? DeployedPackage = null);

public sealed record ServerControlAgentHeartbeatRequest(
    string AgentId,
    string AgentVersion,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ServerControlAgentTargetHeartbeat> Targets,
    IReadOnlyList<Guid>? ActiveDeploymentCommandIds = null,
    int? HostTotalMemoryMiB = null);

public sealed record ServerControlAgentHeartbeatResponse(
    int ImportedTargets,
    DateTimeOffset ReceivedAt);

public sealed record ServerControlCommandClaimRequest(
    string AgentId,
    int Limit);

public sealed record ServerControlCommandDelivery(
    Guid CommandId,
    Guid OperationId,
    string ServerId,
    ServerControlCommandKind Kind,
    int AttemptCount,
    string? ConsoleCommand,
    ServerQuickSettings? Settings,
    ServerPackageDeploymentRequest? PackageDeployment = null);

public sealed record ServerPackageDeploymentRequest(
    Guid ImportId,
    string ProfileId,
    string Version,
    long ArchiveBytes,
    string ArchiveSha256,
    long ExpandedBytes,
    int FileCount,
    bool PreserveWorldData,
    int InitialMemoryMiB,
    int MaximumMemoryMiB);

public sealed record ServerControlCommandClaimResponse(
    IReadOnlyList<ServerControlCommandDelivery> Commands,
    DateTimeOffset ClaimedAt);

public sealed record ServerControlCommandCompletionRequest(
    string AgentId,
    int AttemptCount,
    ServerControlCommandOutcome Outcome,
    string ResultCode,
    string ResultMessage);
