namespace Hechao.Contracts;

public enum OperationalAlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum OperationalAlertStatus
{
    Active,
    Resolved
}

public enum OperationalAlertSource
{
    Api,
    Authentication,
    Distribution,
    Server,
    Certificate,
    Infrastructure
}

public sealed record OperationalAlertRecord(
    string Fingerprint,
    string Code,
    OperationalAlertSource Source,
    OperationalAlertSeverity Severity,
    OperationalAlertStatus Status,
    string Title,
    string Summary,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset LastTransitionAt,
    DateTimeOffset? ResolvedAt,
    long ObservationCount,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedBy,
    long Revision);

public sealed record AdminOperationalAlertSummary(
    DateTimeOffset GeneratedAt,
    int ActiveCount,
    int CriticalCount,
    int WarningCount,
    int UnacknowledgedCount,
    IReadOnlyList<OperationalAlertRecord> Alerts);

public sealed record InternalOperationalAlertEventRequest(
    string Fingerprint,
    string Code,
    OperationalAlertSource Source,
    OperationalAlertSeverity Severity,
    bool Active,
    string Title,
    string Summary,
    DateTimeOffset ObservedAt);

public sealed record InternalOperationalAlertSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<OperationalAlertRecord> Alerts);
