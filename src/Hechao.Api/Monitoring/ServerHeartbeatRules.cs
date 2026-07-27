using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Monitoring;

public static class ServerHeartbeatRules
{
    private static readonly Regex TargetPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CollectorPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    public static Dictionary<string, string[]> Validate(
        ServerHeartbeatBatchRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CapturedAt < now.AddMinutes(-5) ||
            request.CapturedAt > now.AddMinutes(1))
        {
            errors["capturedAt"] = ["The heartbeat timestamp is outside the allowed window."];
        }

        if (string.IsNullOrWhiteSpace(request.CollectorInstance) ||
            !CollectorPattern.IsMatch(request.CollectorInstance))
        {
            errors["collectorInstance"] = ["The collector instance name is invalid."];
        }

        if (request.Servers is null || request.Servers.Count is < 1 or > 64)
        {
            errors["servers"] = ["A heartbeat batch must contain between 1 and 64 targets."];
            return errors;
        }

        if (request.Servers
            .Select(server => server.VelocityTarget)
            .Distinct(StringComparer.Ordinal)
            .Count() != request.Servers.Count)
        {
            errors["servers"] = ["The heartbeat batch contains duplicate Velocity targets."];
        }

        for (var index = 0; index < request.Servers.Count; index++)
        {
            var heartbeat = request.Servers[index];
            var key = $"servers[{index}]";

            if (string.IsNullOrWhiteSpace(heartbeat.VelocityTarget) ||
                !TargetPattern.IsMatch(heartbeat.VelocityTarget))
            {
                errors[key] = ["The Velocity target name is invalid."];
                continue;
            }

            if (heartbeat.OnlinePlayers < 0 ||
                heartbeat.MaxPlayers < 0 ||
                heartbeat.MaxPlayers > 10000 ||
                heartbeat.OnlinePlayers > heartbeat.MaxPlayers ||
                (heartbeat.Online && heartbeat.MaxPlayers == 0) ||
                (!heartbeat.Online && heartbeat.OnlinePlayers != 0))
            {
                errors[key] = ["The player counts are invalid for the reported state."];
                continue;
            }

            if (heartbeat.ProtocolVersion is < 0 or > 100000)
            {
                errors[key] = ["The Minecraft protocol version is invalid."];
                continue;
            }

            if (heartbeat.SoftwareVersion is { Length: > 120 } ||
                heartbeat.SoftwareVersion?.Any(char.IsControl) == true)
            {
                errors[key] = ["The software version is invalid."];
                continue;
            }

            var issues = heartbeat.Issues ?? [];
            if (issues.Count > 16 ||
                issues.Distinct().Count() != issues.Count ||
                issues.Any(issue => !Enum.IsDefined(issue)))
            {
                errors[key] = ["The probe issue list is invalid."];
                continue;
            }

            var hasProcessMetrics =
                heartbeat.ProcessWorkingSetBytes is not null ||
                heartbeat.ProcessPrivateBytes is not null ||
                heartbeat.ProcessCpuPercent is not null ||
                heartbeat.ProcessStartedAt is not null;
            var hasCompleteProcessMetrics =
                heartbeat.ProcessWorkingSetBytes is not null &&
                heartbeat.ProcessPrivateBytes is not null &&
                heartbeat.ProcessCpuPercent is not null &&
                heartbeat.ProcessStartedAt is not null;
            if (hasProcessMetrics != hasCompleteProcessMetrics ||
                heartbeat.ProcessWorkingSetBytes is < 0 or > 17_592_186_044_416 ||
                heartbeat.ProcessPrivateBytes is < 0 or > 17_592_186_044_416 ||
                !IsFiniteInRange(heartbeat.ProcessCpuPercent, 0, 100) ||
                heartbeat.ProcessStartedAt < now.AddYears(-10) ||
                heartbeat.ProcessStartedAt > request.CapturedAt.AddMinutes(1))
            {
                errors[key] = ["The process metrics are invalid."];
                continue;
            }

            var hasDiskMetrics =
                heartbeat.DiskFreeBytes is not null ||
                heartbeat.DiskTotalBytes is not null;
            var hasCompleteDiskMetrics =
                heartbeat.DiskFreeBytes is not null &&
                heartbeat.DiskTotalBytes is not null;
            if (hasDiskMetrics != hasCompleteDiskMetrics ||
                heartbeat.DiskFreeBytes is < 0 or > 1_125_899_906_842_624 ||
                heartbeat.DiskTotalBytes is < 0 or > 1_125_899_906_842_624 ||
                heartbeat.DiskFreeBytes > heartbeat.DiskTotalBytes)
            {
                errors[key] = ["The disk metrics are invalid."];
                continue;
            }

            var hasTickMetrics =
                heartbeat.Tps1m is not null ||
                heartbeat.Tps5m is not null ||
                heartbeat.Tps15m is not null ||
                heartbeat.MsptAverage is not null ||
                heartbeat.MetricsCapturedAt is not null;
            var hasCompleteTickMetrics =
                heartbeat.Tps1m is not null &&
                heartbeat.Tps5m is not null &&
                heartbeat.Tps15m is not null &&
                heartbeat.MsptAverage is not null &&
                heartbeat.MetricsCapturedAt is not null;
            if (hasTickMetrics != hasCompleteTickMetrics ||
                !IsFiniteInRange(heartbeat.Tps1m, 0, 20.1) ||
                !IsFiniteInRange(heartbeat.Tps5m, 0, 20.1) ||
                !IsFiniteInRange(heartbeat.Tps15m, 0, 20.1) ||
                !IsFiniteInRange(heartbeat.MsptAverage, 0, 60_000) ||
                heartbeat.MetricsCapturedAt < request.CapturedAt.AddMinutes(-5) ||
                heartbeat.MetricsCapturedAt > request.CapturedAt.AddMinutes(1) ||
                heartbeat.GcCollectionTimeMilliseconds is < 0 or > 31_536_000_000)
            {
                errors[key] = ["The tick metrics are invalid."];
            }
        }

        return errors;
    }

    private static bool IsFiniteInRange(
        double? value,
        double minimum,
        double maximum) =>
        value is null ||
        (double.IsFinite(value.Value) &&
         value.Value >= minimum &&
         value.Value <= maximum);
}
