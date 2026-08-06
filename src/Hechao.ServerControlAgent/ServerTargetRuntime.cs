using Hechao.Contracts;
using System.Text.Json;

namespace Hechao.ServerControlAgent;

internal sealed class ServerTargetRuntime
{
    private static readonly TimeSpan DefaultSaveFlushDelay =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultStopCommandGracePeriod =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopCompletionTimeout =
        TimeSpan.FromMinutes(3);

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
    private readonly ServerPackageDeployer _packageDeployer;
    private readonly ServerDirectoryAccessGate _serverDirectoryAccessGate = new();
    private readonly ServerDirectoryDeletionManager _directoryDeletionManager;
    private readonly TimeSpan _saveFlushDelay;
    private readonly TimeSpan _stopCommandGracePeriod;
    private readonly int _managedMaximumMemoryMiB;

    internal ServerTargetRuntime(
        ServerControlTargetConfiguration configuration,
        string consoleSubmitScript,
        string backupRoot,
        string runtimeMarkerDirectory,
        bool requiresManagedMarker,
        IProcessRunner processRunner,
        TimeSpan? saveFlushDelay = null,
        TimeSpan? stopCommandGracePeriod = null,
        int? managedMaximumMemoryMiB = null)
    {
        Configuration = configuration;
        _consoleSubmitScript = consoleSubmitScript;
        _backupRoot = backupRoot;
        _runtimeMarkerPath = Path.Combine(
            runtimeMarkerDirectory,
            configuration.ServerId + ".json");
        _requiresManagedMarker = requiresManagedMarker;
        _processRunner = processRunner;
        _managedMaximumMemoryMiB = managedMaximumMemoryMiB ??
            configuration.MaximumAllowedMemoryMiB;
        _packageDeployer = new ServerPackageDeployer(
            configuration,
            backupRoot,
            _serverDirectoryAccessGate,
            _managedMaximumMemoryMiB);
        var hostManagedSnapshotStore = new HostManagedSnapshotStore(
            configuration,
            backupRoot);
        _directoryDeletionManager = new ServerDirectoryDeletionManager(
            configuration,
            _serverDirectoryAccessGate,
            _runtimeMarkerPath,
            hostManagedSnapshotStore);
        _saveFlushDelay = saveFlushDelay ?? DefaultSaveFlushDelay;
        _stopCommandGracePeriod =
            stopCommandGracePeriod ?? DefaultStopCommandGracePeriod;

        if (_saveFlushDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(saveFlushDelay));
        }

        if (_stopCommandGracePeriod < TimeSpan.Zero ||
            _stopCommandGracePeriod >= StopCompletionTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stopCommandGracePeriod));
        }
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
        using (await _serverDirectoryAccessGate.EnterAsync(cancellationToken))
        {
            var deletionState = _directoryDeletionManager.CaptureState();
            var logPath = Configuration.GetLogPath();
            DateTimeOffset? capturedAt = File.Exists(logPath)
                ? File.GetLastWriteTimeUtc(logPath)
                : null;
            var settings = ServerPropertiesEditor.Read(
                Configuration.GetPropertiesPath());
            var memorySettings = JvmMemorySettingsEditor.Read(
                Configuration.GetMemorySettingsPath(),
                _managedMaximumMemoryMiB);
            if (settings is not null && memorySettings is not null)
            {
                settings = settings with
                {
                    InitialMemoryMiB = memorySettings.InitialMemoryMiB,
                    MaximumMemoryMiB = memorySettings.MaximumMemoryMiB,
                    MaximumAllowedMemoryMiB = _managedMaximumMemoryMiB
                };
            }

            return new ServerControlAgentTargetHeartbeat(
                Configuration.ServerId,
                Configuration.ConflictGroup,
                Configuration.Port,
                processId is not null,
                processId,
                settings,
                Configuration.AllowedCommandPrefixes,
                ConsoleTailReader.Read(logPath),
                capturedAt,
                Configuration.PackageDeploymentEnabled,
                Configuration.ServerDeletionEnabled,
                deletionState.FilesPresent,
                deletionState.CleanupPending);
        }
    }

    internal async Task<AgentCommandResult> ExecuteAsync(
        ServerControlCommandDelivery command,
        IReadOnlyList<ServerTargetRuntime> allTargets,
        CancellationToken cancellationToken,
        string? packageArchivePath = null)
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
                await ApplySettingsAsync(
                    command.Settings,
                    cancellationToken),
            ServerControlCommandKind.DeleteServerFiles =>
                await _directoryDeletionManager.DeleteAsync(
                    command.CommandId,
                    FindProcessIdAsync,
                    cancellationToken),
            ServerControlCommandKind.DeployPackage
                when command.PackageDeployment is not null &&
                     packageArchivePath is not null =>
                await _packageDeployer.DeployAsync(
                    command.PackageDeployment,
                    packageArchivePath,
                    FindProcessIdAsync,
                    cancellationToken),
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

        if (!Directory.Exists(Configuration.ServerDirectory))
        {
            return Failed(
                "SERVER_FILES_MISSING",
                "服务端运行目录不存在，无法启动；请先重新部署服务端文件。");
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

        var (Success, Message) = await SubmitConsoleAsync(
            processId.Value,
            "save-all flush",
            cancellationToken);
        if (!Success)
        {
            return Failed("SAVE_FAILED", Message);
        }

        await Task.Delay(_saveFlushDelay, cancellationToken);
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
            _stopCommandGracePeriod,
            cancellationToken);
        if (remaining is null)
        {
            return Succeeded("STOPPED", "服务器已保存并正常停止。");
        }

        if (remaining.Value != processId.Value)
        {
            return Failed(
                "STOP_TARGET_CHANGED",
                "停止期间监听进程发生变化，已拒绝向新进程发送中断。");
        }

        var interrupt = await SubmitConsoleInterruptAsync(
            processId.Value,
            cancellationToken);
        if (!interrupt.Success)
        {
            return Failed("STOP_INTERRUPT_FAILED", interrupt.Message);
        }

        remaining = await WaitForStateAsync(
            shouldBeOnline: false,
            StopCompletionTimeout - _stopCommandGracePeriod,
            cancellationToken);
        return remaining is null
            ? Succeeded(
                "STOPPED_WITH_INTERRUPT",
                "服务器未响应文本停止命令，已通过 JVM 控制台中断保存并停止。")
            : Failed(
                "STOP_TIMEOUT",
                "服务器收到停止命令和控制台中断，但端口未在限定时间内释放。");
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

        var (Success, Message) = await SubmitConsoleAsync(
            processId.Value,
            normalized,
            cancellationToken);
        return Success
            ? Succeeded(
                "COMMAND_SENT",
                "Minecraft 控制台已接收命令；请在日志快照中核对执行结果。")
            : Failed("COMMAND_FAILED", Message);
    }

    private async Task<AgentCommandResult> ApplySettingsAsync(
        ServerQuickSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settings is null ||
            settings.MaxPlayers is < 1 or > 1000 ||
            settings.ViewDistance is < 2 or > 32 ||
            settings.SimulationDistance is < 2 or > 32 ||
            settings.Difficulty is not ("peaceful" or "easy" or "normal" or "hard") ||
            settings.InitialMemoryMiB is not int initialMemoryMiB ||
            settings.MaximumMemoryMiB is not int maximumMemoryMiB ||
            initialMemoryMiB is < 512 or > 65536 ||
            maximumMemoryMiB is < 512 or > 65536 ||
            initialMemoryMiB % 256 != 0 ||
            maximumMemoryMiB % 256 != 0 ||
            initialMemoryMiB > maximumMemoryMiB ||
            maximumMemoryMiB > _managedMaximumMemoryMiB)
        {
            return Failed("INVALID_SETTINGS", "服务器快捷设置无效。");
        }

        using (await _serverDirectoryAccessGate.EnterAsync(cancellationToken))
        {
            var propertiesPath = Configuration.GetPropertiesPath();
            var memorySettingsPath = Configuration.GetMemorySettingsPath();
            byte[]? originalProperties = null;
            byte[]? originalMemorySettings = null;
            try
            {
                JvmMemorySettingsEditor.EnsureCanApply(
                    memorySettingsPath,
                    initialMemoryMiB,
                    maximumMemoryMiB,
                    _managedMaximumMemoryMiB);
                originalProperties = SharedFileReader.ReadAllBytes(propertiesPath);
                originalMemorySettings =
                    SharedFileReader.ReadAllBytes(memorySettingsPath);
                ServerPropertiesEditor.Apply(
                    propertiesPath,
                    _backupRoot,
                    Configuration.ServerId,
                    settings);
                JvmMemorySettingsEditor.Apply(
                    memorySettingsPath,
                    _backupRoot,
                    Configuration.ServerId,
                    initialMemoryMiB,
                    maximumMemoryMiB,
                    _managedMaximumMemoryMiB);
                return Succeeded(
                    "SETTINGS_APPLIED",
                    "快捷设置和 JVM 启动内存已写入并备份；运行中的服务器不会自动重启，内存将在下次启动生效。");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or NotSupportedException)
            {
                var rollbackErrors = new List<string>();
                TryRestoreFile(
                    propertiesPath,
                    originalProperties,
                    rollbackErrors);
                TryRestoreFile(
                    memorySettingsPath,
                    originalMemorySettings,
                    rollbackErrors);
                var rollbackMessage = rollbackErrors.Count == 0
                    ? "修改已自动回滚。"
                    : $"自动回滚失败：{string.Join("；", rollbackErrors)}";
                return Failed(
                    rollbackErrors.Count == 0
                        ? "SETTINGS_WRITE_FAILED"
                        : "SETTINGS_ROLLBACK_FAILED",
                    AgentLog.Sanitize(
                        $"{exception.Message} {rollbackMessage}",
                        1000));
            }
        }
    }

    private static void TryRestoreFile(
        string path,
        byte[]? originalBytes,
        ICollection<string> errors)
    {
        if (originalBytes is null)
        {
            return;
        }

        var temporary = path + $".rollback-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, originalBytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(temporary)}: {exception.Message}");
            }
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

    private async Task<(bool Success, string Message)> SubmitConsoleInterruptAsync(
        int processId,
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
                "-Interrupt"
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
