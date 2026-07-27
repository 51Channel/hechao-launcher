namespace Hechao.Api.Monitoring;

public sealed class ServerHeartbeatOptions
{
    public const string SectionName = "ServerHeartbeats";

    public string InternalTokenSha256 { get; init; } = string.Empty;

    public int FreshnessSeconds { get; init; } = 180;

    public int RuntimeHistoryRetentionDays { get; init; } = 30;

    public int RuntimeHistoryCleanupHours { get; init; } = 6;
}
