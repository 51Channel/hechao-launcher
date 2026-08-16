namespace Hechao.Modpack;

public enum ModpackLayoutKind
{
    Unknown,
    Canonical,
    Combined,
    Modrinth,
    CurseForge,
    ClientOnly,
    ServerOnly
}

public enum ModpackFileSide
{
    Client,
    Server,
    Shared,
    Ignored,
    Rejected
}

public enum ModpackIssueSeverity
{
    Information,
    Warning,
    Blocking
}

public enum DeploymentCheckStatus
{
    Passed,
    Warning,
    Blocking
}

public enum ServerCoreKind
{
    Unknown,
    Vanilla,
    Paper,
    Purpur,
    Fabric,
    Forge,
    NeoForge,
    Arclight
}

public enum DeploymentReadiness
{
    Compliant,
    ReviewRequired,
    Blocked
}

public sealed record ModpackIssue(
    string Code,
    ModpackIssueSeverity Severity,
    string Message,
    string? Path = null);

public sealed record ModpackFileRecord(
    string SourcePath,
    string TargetPath,
    ModpackFileSide Side,
    long Size,
    string Sha256);

public sealed record ModpackDetectedMetadata(
    string SuggestedProfileId,
    string DisplayName,
    string Version,
    string MinecraftVersion,
    int JavaMajorVersion,
    string Loader,
    string LoaderVersion,
    int? MaximumPlayers,
    string? ServerLaunchPath);

public sealed record ModpackArchivePart(
    string Path,
    string Sha256,
    long ArchiveBytes,
    long ExpandedBytes,
    int FileCount);

public sealed record ServerDeploymentCheck(
    string Code,
    DeploymentCheckStatus Status,
    string Title,
    string Message,
    string? Path = null,
    string? Remediation = null);

public sealed record ServerDeploymentInspection(
    string? DeclaredCore,
    ServerCoreKind ExpectedCore,
    IReadOnlyList<ServerCoreKind> DetectedCores,
    ServerCoreKind LaunchCore,
    string? LaunchPath,
    string? LaunchCommand,
    IReadOnlyList<ServerDeploymentCheck> Checks)
{
    public bool HasBlockingIssues =>
        Checks.Any(check => check.Status == DeploymentCheckStatus.Blocking);
}

public sealed record ModpackArchivePartSummary(
    long ExpandedBytes,
    int FileCount);

public sealed record ModpackDeploymentReport(
    int SchemaVersion,
    DateTimeOffset InspectedAtUtc,
    string ArchiveName,
    long ArchiveBytes,
    string ArchiveSha256,
    DeploymentReadiness Readiness,
    ModpackLayoutKind Layout,
    ModpackDetectedMetadata Metadata,
    ModpackArchivePartSummary? Client,
    ModpackArchivePartSummary? Server,
    int SharedFileCount,
    ServerDeploymentInspection? ServerDeployment,
    IReadOnlyList<ServerDeploymentCheck> Checks)
{
    public int BlockingCount =>
        Checks.Count(check => check.Status == DeploymentCheckStatus.Blocking);

    public int WarningCount =>
        Checks.Count(check => check.Status == DeploymentCheckStatus.Warning);

    public int PassedCount =>
        Checks.Count(check => check.Status == DeploymentCheckStatus.Passed);
}

public sealed record ModpackAnalysisResult(
    ModpackLayoutKind Layout,
    ModpackDetectedMetadata Metadata,
    ModpackArchivePart? Client,
    ModpackArchivePart? Server,
    IReadOnlyList<ModpackFileRecord> Files,
    IReadOnlyList<ModpackIssue> Issues)
{
    public ServerDeploymentInspection? ServerDeployment { get; init; }

    public int SharedFileCount { get; init; }

    public bool HasBlockingIssues =>
        Issues.Any(issue => issue.Severity == ModpackIssueSeverity.Blocking);
}

public sealed record ModpackInspectionLimits
{
    public int MaximumEntries { get; init; } = 50_000;
    public long MaximumExpandedBytes { get; init; } = 20L * 1024 * 1024 * 1024;
    public long MaximumEntryBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int MaximumCompressionRatio { get; init; } = 250;
    public int MaximumPathLength { get; init; } = 400;

    public void Validate()
    {
        if (MaximumEntries is < 1 or > 200_000 ||
            MaximumExpandedBytes is < 1024 or > 100L * 1024 * 1024 * 1024 ||
            MaximumEntryBytes < 1024 ||
            MaximumEntryBytes > MaximumExpandedBytes ||
            MaximumCompressionRatio is < 10 or > 10_000 ||
            MaximumPathLength is < 120 or > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ModpackInspectionLimits),
                "The modpack inspection limits are invalid.");
        }
    }
}

public sealed record HechaoModpackDescriptor
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string MinecraftVersion { get; init; } = string.Empty;
    public int JavaMajorVersion { get; init; }
    public string Loader { get; init; } = string.Empty;
    public string LoaderVersion { get; init; } = string.Empty;
    public string ServerCore { get; init; } = string.Empty;
    public string ClientRoot { get; init; } = "client";
    public string ServerRoot { get; init; } = "server";
    public string SharedRoot { get; init; } = "shared";
}
