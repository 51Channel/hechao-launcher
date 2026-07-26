namespace Hechao.Api.Telemetry;

public sealed class LauncherTelemetryOptions
{
    public const string SectionName = "LauncherTelemetry";

    public int RetentionDays { get; init; } = 30;
    public int CleanupHours { get; init; } = 6;
}
