using Hechao.Contracts;
using System.Text.Json;

namespace Hechao.ServerControlAgent;

internal sealed class ServerTargetRuntime
{
    private static readonly HashSet<string> ReservedTerminalCommands =
        new(StringComparer.Ordinal)
        {
            "stop",
            "restart",
            "shutdown",
            "end"
        };

    private readonly string _consoleSubmitScript;
    private readonly string _backupRoot;
    private readonly string _runtimeMarkerPath;
    private readonly bool _requiresManagedMarker;
    private readonly IProcessRunner _processRunner;

    internal ServerTargetRuntime(
        ServerControlTargetConfiguration configuration,
        string consoleSubmitScript,
        string backupRoot,
        string runtimeMarkerDirectory,
        bool requiresManagedMarker,
        IProcessRunner processRunner)
    {
        Configuration = configuration;
        _consoleSubmitScript = consoleSubmitScript;
        _backupRoot = backupRoot;
        _runtimeMarkerPath = Path.Combine(
            runtimeMarkerDirectory,
            configuration.ServerId + ".json");
        _requiresManagedMarker = requiresManagedMarker;
        _processRunner = processRunner;
    }

    internal ServerControlTargetConfiguration Configuration { get; }

    internal async Task<int?> FindProcessIdAsync(
        CancellationToken cancellationToken)
    {
        if (_requiresManagedMarker)
        {
            return await FindManagedProcessIdAsync(cancellationToken);
        }

        return await FindPortOwnerProcessIdAsync(cancellationToken);
    }

    private async Task<int?> FindPortOwnerProcessIdAsync(
        CancellationToken cancellationToken)
    {
        var script =
            "$p=(Get-NetTCPConnection -State Listen -LocalPort " +
            Configuration.Port.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            " -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess);" +
            "if($p){[Console]::Out.Write($p)};" +
            "exit 0";
        var result = await _processRunner.RunAsync(
            "pwsh.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                FormatProbeFailure(
                    "Unable to inspect the configured listening port",
                    result));
        }

        return int.TryParse(
            result.StandardOutput.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var processId) &&
            processId > 0
                ? processId
                : null;
    }

    private async Task<int?> FindManagedProcessIdAsync(
        CancellationToken cancellationToken)
    {
        var marker = TryReadRuntimeMarker();
        if (marker is null)
        {
            return null;
        }

        var runnerProcessId = marker.RunnerProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var runnerStartedAtUtcTicks = marker.RunnerStartedAtUtcTicks.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var port = Configuration.Port.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var script =
            "$root=Get-Process -Id " + runnerProcessId +
            " -ErrorAction SilentlyContinue;" +
            "if($null -eq $root){exit 0};" +
            "$ticks=$root.StartTime.ToUniversalTime().Ticks;" +
            "if([Math]::Abs($ticks-" + runnerStartedAtUtcTicks +
            ") -gt 50000000){exit 0};" +
            "$owner=(Get-NetTCPConnection -State Listen -LocalPort " + port +
            " -ErrorAction SilentlyContinue | Select-Object -First 1 " +
            "-ExpandProperty OwningProcess);" +
            "if(-not $owner){exit 0};" +
            "$current=[int]$owner;" +
            "for($i=0;$i -lt 16 -and $current -gt 0;$i++){" +
            "if($current -eq " + runnerProcessId +
            "){[Console]::Out.Write($owner);exit 0};" +
            "$row=Get-CimInstance Win32_Process -Filter " +
            "\"ProcessId = $current\" -ErrorAction SilentlyContinue;" +
            "if($null -eq $row){exit 0};" +
            "$current=[int]$row.ParentProcessId" +
            "};" +
            "exit 0";
        var result = await _processRunner.RunAsync(
            "pwsh.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                FormatProbeFailure(
                    "Unable to verify the managed server process",
                    result));
        }

        return int.TryParse(
            result.StandardOutput.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var processId) &&
            processId > 0
                ? processId
                : null;
    }

    private RuntimeMarker? TryReadRuntimeMarker()
    {
        try
        {
            if (!File.Exists(_runtimeMarkerPath))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<RuntimeMarker>(
                File.ReadAllText(_runtimeMarkerPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (marker is null ||
                !string.Equals(
                    marker.ServerId,
                    Configuration.ServerId,
                    StringComparison.Ordinal) ||
                marker.RunnerProcessId <= 0 ||
                marker.RunnerStartedAtUtcTicks <= 0 ||
                !string.Equals(
                    Path.GetFullPath(marker.ServerDirectory),
                    Path.GetFullPath(Configuration.ServerDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return marker;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal async Task<ServerControlAgentTargetHeartbeat> CaptureHeartbeatAsync(
        CancellationToken cancellationToken)
    {
        var processId = await FindProcessIdAsync(cancellationToken);
        var logPath = Configuration.GetLogPath();
        DateTimeOffset? capturedAt = File.Exists(logPath)
            ? File.GetLastWriteTimeUtc(logPath)
            : null;
        return new ServerControlAgentTargetHeartbeat(
            Configuration.ServerId,
            Configuration.ConflictGroup,
            Configuration.Port,
            processId is not null,
            processId,
            ServerPropertiesEditor.Read(Configuration.GetPropertiesPath()),
            Configuration.AllowedCommandPrefixes,
            ConsoleTailReader.Read(logPath),
            capturedAt);
    }

    internal async Task<AgentCommandResult> ExecuteAsync(
        ServerControlCommandDelivery command,
        IReadOnlyList<ServerTargetRuntime> allTargets,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                command.ServerId,
                Configuration.ServerId,
                StringComparison.Ordinal))
        {
            return Failed(
                "TARGET_MISMATCH",
                "命令目标与本机白名单不一致。");
        }

        return command.Kind switch
        {
            ServerControlCommandKind.Start =>
                await StartAsync(allTargets, cancellationToken),
            ServerControlCommandKind.Stop =>
                await StopAsync(cancellationToken),
            ServerControlCommandKind.ConsoleCommand =>
                await RunConsoleCommandAsync(
                    command.ConsoleCommand,
                    cancellationToken),
            ServerControlCommandKind.ApplySettings =>
                ApplySettings(command.Settings),
            _ => Failed("UNSUPPORTED_ACTION", "代理不支持该控制动作。")
        };
    }

    private async Task<AgentCommandResult> StartAsync(
        IReadOnlyList<ServerTargetRuntime> allTargets,
        CancellationToken cancellationToken)
    {
        if (await FindProcessIdAsync(cancellationToken) is not null)
        {
            return Succeeded("ALREADY_RUNNING", "服务器已经在运行。");
        }

        if (Configuration.ConflictGroup is not null)
        {
            foreach (var conflict in allTargets.Where(target =>
                         !ReferenceEquals(target, this) &&
                         string.Equals(
                             target.Configuration.ConflictGroup,
                             Configuration.ConflictGroup,
                             StringComparison.Ordinal)))
            {
                if (await conflict.FindProcessIdAsync(cancellationToken) is not null)
                {
                    return new AgentCommandResult(
                        ServerControlCommandOutcome.Conflict,
                        "LOCAL_CONFLICT_ACTIVE",
                        $"冲突服务器 {conflict.Configuration.ServerId} 仍在运行，已拒绝启动。");
                }
            }
        }

        var unknownPortOwner =
            await FindPortOwnerProcessIdAsync(cancellationToken);
        if (unknownPortOwner is not null)
        {
            return new AgentCommandResult(
                ServerControlCommandOutcome.Conflict,
                "LOCAL_PORT_OCCUPIED",
                $"端口 {Configuration.Port} 已被未归属到当前服务端的进程占用，已拒绝启动。");
        }

        var start = await _processRunner.RunAsync(
            "schtasks.exe",
            ["/Run", "/TN", Configuration.StartTaskName],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (start.ExitCode != 0)
        {
            return Failed(
                "START_TASK_FAILED",
                "服务器启动计划任务执行失败：" +
                AgentLog.Sanitize(start.StandardError, 600));
        }

        var processId = await WaitForStateAsync(
            shouldBeOnline: true,
            TimeSpan.FromMinutes(3),
            cancellationToken);
        return processId is null
            ? Failed(
                "START_TIMEOUT",
                "启动任务已执行，但服务器未在限定时间内监听端口。")
            : Succeeded(
                "STARTED",
                $"服务器已启动，进程号 {processId.Value}。");
    }

    private async Task<AgentCommandResult> StopAsync(
        CancellationToken cancellationToken)
    {
        var processId = await FindProcessIdAsync(cancellationToken);
        if (processId is null)
        {
            return Succeeded("ALREADY_STOPPED", "服务器已经停止。");
        }

        var save = await SubmitConsoleAsync(
            processId.Value,
            "save-all flush",
            cancellationToken);
        if (!save.Success)
        {
            return Failed("SAVE_FAILED", save.Message);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var stop = await SubmitConsoleAsync(
            processId.Value,
            "stop",
            cancellationToken);
        if (!stop.Success)
        {
            return Failed("STOP_COMMAND_FAILED", stop.Message);
        }

        var remaining = await WaitForStateAsync(
            shouldBeOnline: false,
            TimeSpan.FromMinutes(3),
            cancellationToken);
        return remaining is null
            ? Succeeded("STOPPED", "服务器已保存并正常停止。")
            : Failed(
                "STOP_TIMEOUT",
                "服务器收到停止命令，但端口未在限定时间内释放。");
    }

    private async Task<AgentCommandResult> RunConsoleCommandAsync(
        string? command,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAllowedCommand(command, out var normalized))
        {
            return Failed(
                "COMMAND_NOT_ALLOWED",
                "控制台命令不在本机允许列表中。");
        }

        var processId = await FindProcessIdAsync(cancellationToken);
        if (processId is null)
        {
            return Failed("SERVER_OFFLINE", "服务器未运行。");
        }

        var result = await SubmitConsoleAsync(
            processId.Value,
            normalized,
            cancellationToken);
        return result.Success
            ? Succeeded(
                "COMMAND_SENT",
                "Minecraft 控制台已接收命令；请在日志快照中核对执行结果。")
            : Failed("COMMAND_FAILED", result.Message);
    }

    private AgentCommandResult ApplySettings(ServerQuickSettings? settings)
    {
        if (settings is null ||
            settings.MaxPlayers is < 1 or > 1000 ||
            settings.ViewDistance is < 2 or > 32 ||
            settings.SimulationDistance is < 2 or > 32 ||
            settings.Difficulty is not ("peaceful" or "easy" or "normal" or "hard"))
        {
            return Failed("INVALID_SETTINGS", "服务器快捷设置无效。");
        }

        try
        {
            ServerPropertiesEditor.Apply(
                Configuration.GetPropertiesPath(),
                _backupRoot,
                Configuration.ServerId,
                settings);
            return Succeeded(
                "SETTINGS_APPLIED",
                "快捷设置已原子写入并备份；需要重启服务器的项目将在下次启动生效。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException)
        {
            return Failed(
                "SETTINGS_WRITE_FAILED",
                AgentLog.Sanitize(exception.Message, 1000));
        }
    }

    private bool TryNormalizeAllowedCommand(
        string? command,
        out string normalized)
    {
        normalized = command?.Trim().TrimStart('/') ?? string.Empty;
        if (normalized.Length is < 1 or > 240 ||
            normalized.Any(character =>
                character is '\r' or '\n' or '\0' ||
                (char.IsControl(character) && character != '\t')))
        {
            return false;
        }

        var separator = normalized.IndexOfAny([' ', '\t']);
        var prefix = (separator < 0 ? normalized : normalized[..separator])
            .ToLowerInvariant();
        return !ReservedTerminalCommands.Contains(prefix) &&
               Configuration.AllowedCommandPrefixes.Contains(
                   prefix,
                   StringComparer.Ordinal);
    }

    private async Task<(bool Success, string Message)> SubmitConsoleAsync(
        int processId,
        string command,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "pwsh.exe",
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                _consoleSubmitScript,
                "-ProcessId",
                processId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                "-Command",
                command
            ],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        var message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return (
            result.ExitCode == 0,
            AgentLog.Sanitize(message, 1000));
    }

    private async Task<int?> WaitForStateAsync(
        bool shouldBeOnline,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var processId = await FindProcessIdAsync(cancellationToken);
            if ((processId is not null) == shouldBeOnline)
            {
                return processId;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return await FindProcessIdAsync(cancellationToken);
    }

    private static AgentCommandResult Succeeded(string code, string message) =>
        new(ServerControlCommandOutcome.Succeeded, code, message);

    private static AgentCommandResult Failed(string code, string message) =>
        new(ServerControlCommandOutcome.Failed, code, message);

    private static string FormatProbeFailure(
        string summary,
        ProcessRunResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        var safeDetail = AgentLog.Sanitize(detail, 600);
        return string.IsNullOrWhiteSpace(safeDetail)
            ? $"{summary} (exit {result.ExitCode})."
            : $"{summary} (exit {result.ExitCode}): {safeDetail}";
    }

    private sealed record RuntimeMarker(
        string ServerId,
        int RunnerProcessId,
        long RunnerStartedAtUtcTicks,
        string ServerDirectory);
}
