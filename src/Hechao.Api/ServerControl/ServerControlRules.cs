using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.ServerControl;

public static partial class ServerControlRules
{
    private static readonly HashSet<string> Difficulties =
        new(StringComparer.Ordinal)
        {
            "peaceful",
            "easy",
            "normal",
            "hard"
        };

    public static bool IsValidAgentId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AgentIdRegex().IsMatch(value);

    public static bool IsValidServerId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        ServerIdRegex().IsMatch(value);

    public static Dictionary<string, string[]> Validate(
        string serverId,
        AdminServerControlRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidServerId(serverId))
        {
            errors["serverId"] = ["服务器 ID 无效。"];
        }

        if (!string.Equals(
                request.Confirmation?.Trim(),
                serverId,
                StringComparison.Ordinal))
        {
            errors["confirmation"] = ["请输入完整服务器 ID 进行二次确认。"];
        }

        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 4 or > 500)
        {
            errors["reason"] = ["操作原因必须为 4 到 500 个字符。"];
        }

        if (request.Action == ServerControlAction.ConsoleCommand)
        {
            if (!IsValidConsoleCommand(request.ConsoleCommand))
            {
                errors["consoleCommand"] =
                    ["控制台命令必须是 1 到 240 个字符的单行 Minecraft 命令。"];
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.ConsoleCommand))
        {
            errors["consoleCommand"] = ["当前操作不能携带控制台命令。"];
        }

        if (request.Action == ServerControlAction.ApplySettings)
        {
            if (request.Settings is null ||
                !IsValidSettings(request.Settings, requireMemory: true))
            {
                errors["settings"] = ["服务器快捷设置或启动内存无效。"];
            }
        }
        else if (request.Settings is not null)
        {
            errors["settings"] = ["当前操作不能携带服务器设置。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        ServerControlAgentHeartbeatRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidAgentId(request.AgentId))
        {
            errors["agentId"] = ["代理 ID 无效。"];
        }

        if (string.IsNullOrWhiteSpace(request.AgentVersion) ||
            request.AgentVersion.Length > 40)
        {
            errors["agentVersion"] = ["代理版本无效。"];
        }

        if (request.CapturedAt == default ||
            request.Targets.Count is < 1 or > 32 ||
            request.Targets.Select(target => target.ServerId)
                .Distinct(StringComparer.Ordinal)
                .Count() != request.Targets.Count)
        {
            errors["targets"] = ["代理目标列表无效。"];
            return errors;
        }

        if (request.Targets.Any(target =>
                !IsValidServerId(target.ServerId) ||
                target.Port is < 1 or > 65535 ||
                target.ProcessId is <= 0 ||
                (target.Online && target.ProcessId is null) ||
                (target.ConflictGroup is not null &&
                 !ConflictGroupRegex().IsMatch(target.ConflictGroup)) ||
                target.AllowedCommandPrefixes.Count is < 1 or > 64 ||
                target.AllowedCommandPrefixes.Any(prefix =>
                    !CommandPrefixRegex().IsMatch(prefix)) ||
                target.ConsoleTail.Length > 65536 ||
                target.ConsoleTail.Any(character =>
                    character == '\0' ||
                    (char.IsControl(character) &&
                     character is not '\r' and not '\n' and not '\t')) ||
                (target.Settings is not null &&
                 !IsValidSettings(target.Settings))))
        {
            errors["targets"] = ["代理目标包含无效字段。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        ServerControlCommandClaimRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidAgentId(request.AgentId))
        {
            errors["agentId"] = ["代理 ID 无效。"];
        }

        if (request.Limit is < 1 or > 8)
        {
            errors["limit"] = ["单次领取数量必须在 1 到 8 之间。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        ServerControlCommandCompletionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidAgentId(request.AgentId))
        {
            errors["agentId"] = ["代理 ID 无效。"];
        }

        if (request.AttemptCount < 1)
        {
            errors["attemptCount"] = ["尝试次数无效。"];
        }

        if (!ResultCodeRegex().IsMatch(request.ResultCode ?? string.Empty))
        {
            errors["resultCode"] = ["结果代码无效。"];
        }

        if (string.IsNullOrWhiteSpace(request.ResultMessage) ||
            request.ResultMessage.Trim().Length > 2000)
        {
            errors["resultMessage"] = ["结果说明必须为 1 到 2000 个字符。"];
        }

        return errors;
    }

    public static bool IsValidSettings(
        ServerQuickSettings settings,
        bool requireMemory = false)
    {
        var hasNoMemorySettings =
            settings.InitialMemoryMiB is null &&
            settings.MaximumMemoryMiB is null &&
            settings.MaximumAllowedMemoryMiB is null;
        var hasValidMemorySettings =
            settings.InitialMemoryMiB is int initialMemoryMiB &&
            settings.MaximumMemoryMiB is int maximumMemoryMiB &&
            settings.MaximumAllowedMemoryMiB is int maximumAllowedMemoryMiB &&
            initialMemoryMiB is >= 512 and <= 65536 &&
            maximumMemoryMiB is >= 512 and <= 65536 &&
            maximumAllowedMemoryMiB is >= 512 and <= 65536 &&
            initialMemoryMiB % 256 == 0 &&
            maximumMemoryMiB % 256 == 0 &&
            maximumAllowedMemoryMiB % 256 == 0 &&
            initialMemoryMiB <= maximumMemoryMiB &&
            maximumMemoryMiB <= maximumAllowedMemoryMiB;

        return settings.MaxPlayers is >= 1 and <= 1000 &&
            settings.ViewDistance is >= 2 and <= 32 &&
            settings.SimulationDistance is >= 2 and <= 32 &&
            Difficulties.Contains(settings.Difficulty) &&
            (hasValidMemorySettings || (!requireMemory && hasNoMemorySettings));
    }

    public static bool IsValidConsoleCommand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 240 ||
            value.Any(character =>
                character is '\r' or '\n' or '\0' ||
                (char.IsControl(character) && character != '\t')))
        {
            return false;
        }

        var command = value.TrimStart('/');
        return command.Length > 0;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConflictGroupRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9:_-]{0,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommandPrefixRegex();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,79}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResultCodeRegex();
}
