namespace Hechao.Api.Monitoring;

public sealed class ServerHeartbeatOptions
{
    public const string SectionName = "ServerHeartbeats";

    public string InternalTokenSha256 { get; init; } = string.Empty;

    public int FreshnessSeconds { get; init; } = 180;
}
