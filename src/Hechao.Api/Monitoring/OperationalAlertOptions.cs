namespace Hechao.Api.Monitoring;

public sealed class OperationalAlertOptions
{
    public const string SectionName = "OperationalAlerts";

    public bool Enabled { get; init; } = true;
    public string InternalTokenSha256 { get; init; } = string.Empty;
    public int EvaluationSeconds { get; init; } = 60;
    public int EvaluationWindowMinutes { get; init; } = 15;
    public int RequestMetricsRetentionDays { get; init; } = 30;
}
