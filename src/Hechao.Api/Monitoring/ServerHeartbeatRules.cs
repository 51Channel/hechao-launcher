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
            }
        }

        return errors;
    }
}
