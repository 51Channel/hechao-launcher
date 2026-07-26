namespace Hechao.Contracts;

public enum LauncherTelemetryEventType
{
    LauncherStarted,
    Install,
    Repair,
    Rollback,
    Launch,
    GameExit
}

public enum LauncherTelemetryOutcome
{
    Success,
    Failure,
    Canceled
}

public enum LauncherTelemetryFailureCode
{
    None,
    UserCanceled,
    AuthenticationRequired,
    ProfileUnavailable,
    ApiUnavailable,
    SignatureInvalid,
    IntegrityFailed,
    InsufficientDiskSpace,
    InstallBusy,
    RuntimePreparationFailed,
    NetworkUnavailable,
    IoFailure,
    RollbackUnavailable,
    MinecraftIdentityRequired,
    MicrosoftReauthenticationRequired,
    MicrosoftNotConfigured,
    MicrosoftCanceled,
    MicrosoftAccountMismatch,
    MicrosoftSignInFailed,
    MinecraftOwnership,
    MinecraftSessionExpired,
    LaunchAuthorizationFailed,
    GameAlreadyRunning,
    InvalidProfile,
    InvalidJavaSelection,
    ProcessCreationFailed,
    GameExitedNonZero,
    Unexpected
}

public sealed record LauncherTelemetryEvent(
    Guid EventId,
    LauncherTelemetryEventType Type,
    LauncherTelemetryOutcome Outcome,
    LauncherTelemetryFailureCode FailureCode,
    string LauncherVersion,
    DateTimeOffset OccurredAt,
    string? ProfileId,
    string? ProfileVersion,
    int? DurationMilliseconds,
    long? Bytes);

public sealed record LauncherTelemetryBatchRequest(
    IReadOnlyList<LauncherTelemetryEvent> Events);

public sealed record LauncherTelemetryBatchResponse(
    int Accepted,
    int Duplicates);

public sealed record AdminLauncherTelemetryOperationSummary(
    long Attempts,
    long Succeeded,
    long Failed,
    long Canceled,
    long Bytes,
    double FailureRate);

public sealed record AdminLauncherVersionUsage(
    string LauncherVersion,
    long Users);

public sealed record AdminProfileVersionUsage(
    string ProfileId,
    string ProfileVersion,
    long Users,
    long Events);

public sealed record AdminLauncherTelemetryFailureSummary(
    LauncherTelemetryEventType Type,
    LauncherTelemetryFailureCode FailureCode,
    long Count);

public sealed record AdminLauncherTelemetrySummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int WindowHours,
    long EventCount,
    long UniqueUsers,
    AdminLauncherTelemetryOperationSummary Downloads,
    AdminLauncherTelemetryOperationSummary Launches,
    IReadOnlyList<AdminLauncherVersionUsage> LauncherVersions,
    IReadOnlyList<AdminProfileVersionUsage> ProfileVersions,
    IReadOnlyList<AdminLauncherTelemetryFailureSummary> Failures);
