using Hechao.Contracts;
using System.Text.Json;

namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerTargetRuntimeTests
{
    [Fact]
    public async Task DeleteServerFiles_RemovesOnlyStoppedConfiguredDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-server-delete-" + Guid.NewGuid().ToString("N"));
        var serverDirectory = Path.Combine(root, "activity");
        var externalBackup = Path.Combine(root, "backups", "world.zip");
        Directory.CreateDirectory(Path.Combine(serverDirectory, "world"));
        Directory.CreateDirectory(Path.GetDirectoryName(externalBackup)!);
        File.WriteAllText(
            Path.Combine(serverDirectory, "world", "level.dat"),
            "world-data");
        File.WriteAllText(
            Path.Combine(serverDirectory, "forwarding.secret"),
            "host-secret");
        File.WriteAllText(externalBackup, "backup-data");
        var agentBackupRoot = Path.Combine(root, "agent-backups");
        var runner = new RecordingProcessRunner((_, _) =>
            new ProcessRunResult(0, string.Empty, string.Empty));
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            serverDirectory: serverDirectory,
            serverDeletionEnabled: true,
            packageDeploymentEnabled: true,
            hostManagedRelativePaths: ["forwarding.secret"],
            backupRoot: agentBackupRoot,
            runtimeMarkerDirectory: Path.Combine(root, "runtime"));
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.DeleteServerFiles,
            1,
            null,
            null);

        try
        {
            var result = await runtime.ExecuteAsync(
                command,
                [runtime],
                CancellationToken.None);

            Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
            Assert.Equal("SERVER_FILES_DELETED", result.ResultCode);
            Assert.False(Directory.Exists(serverDirectory));
            Assert.True(File.Exists(externalBackup));
            Assert.Equal(
                "host-secret",
                File.ReadAllText(Path.Combine(
                    agentBackupRoot,
                    "host-managed",
                    "activity",
                    "forwarding.secret")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteServerFiles_RejectsRunningServerWithoutMovingFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-server-delete-running-" + Guid.NewGuid().ToString("N"));
        var serverDirectory = Path.Combine(root, "activity");
        Directory.CreateDirectory(serverDirectory);
        File.WriteAllText(Path.Combine(serverDirectory, "server.jar"), "jar");
        var runner = new RecordingProcessRunner((_, _) =>
            new ProcessRunResult(0, "1234", string.Empty));
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            serverDirectory: serverDirectory,
            serverDeletionEnabled: true,
            runtimeMarkerDirectory: Path.Combine(root, "runtime"));
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.DeleteServerFiles,
            1,
            null,
            null);

        try
        {
            var result = await runtime.ExecuteAsync(
                command,
                [runtime],
                CancellationToken.None);

            Assert.Equal(ServerControlCommandOutcome.Conflict, result.Outcome);
            Assert.Equal("SERVER_STILL_RUNNING", result.ResultCode);
            Assert.True(File.Exists(Path.Combine(serverDirectory, "server.jar")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConsoleCommand_UsesOnlyFixedPowerShellBridge()
    {
        var runner = new RecordingProcessRunner((executable, arguments) =>
        {
            if (arguments.Contains("-Command"))
            {
                return new ProcessRunResult(0, "1234", string.Empty);
            }

            return new ProcessRunResult(0, "ok", string.Empty);
        });
        var runtime = CreateRuntime("activity", 25568, null, runner);
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.ConsoleCommand,
            1,
            "say hello",
            null);

        var result = await runtime.ExecuteAsync(
            command,
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.All(runner.Calls, call => Assert.Equal("pwsh.exe", call.Executable));
        Assert.Contains(
            runner.Calls,
            call =>
                call.Arguments.Contains("-File") &&
                call.Arguments.Contains("say hello"));
    }

    [Theory]
    [InlineData("op 51Channel")]
    [InlineData("luckperms user 51Channel info")]
    [InlineData("minecraft:gamemode creative 51Channel")]
    public async Task ConsoleCommand_WildcardAllowsMinecraftAndPluginCommands(
        string consoleCommand)
    {
        var runner = new RecordingProcessRunner((_, arguments) =>
            arguments.Contains("-Command")
                ? new ProcessRunResult(0, "1234", string.Empty)
                : new ProcessRunResult(0, "ok", string.Empty));
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            ["*"]);
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.ConsoleCommand,
            1,
            consoleCommand,
            null);

        var result = await runtime.ExecuteAsync(
            command,
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Contains(
            runner.Calls,
            call => call.Arguments.Contains(consoleCommand));
    }

    [Fact]
    public async Task ConsoleCommand_AlwaysRejectsStopEvenIfConfigured()
    {
        var runner = new RecordingProcessRunner((_, _) =>
            new ProcessRunResult(0, "1234", string.Empty));
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            ["*"]);
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.ConsoleCommand,
            1,
            "stop",
            null);

        var result = await runtime.ExecuteAsync(
            command,
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("COMMAND_NOT_ALLOWED", result.ResultCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Stop_DoesNotInterruptWhenTextCommandStopsServer()
    {
        var stopped = false;
        var runner = new RecordingProcessRunner((_, arguments) =>
        {
            if (arguments.Contains("-File"))
            {
                if (arguments.Contains("stop"))
                {
                    stopped = true;
                }

                return new ProcessRunResult(0, "ok", string.Empty);
            }

            return new ProcessRunResult(
                0,
                stopped ? string.Empty : "1234",
                string.Empty);
        });
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            saveFlushDelay: TimeSpan.Zero,
            stopCommandGracePeriod: TimeSpan.Zero);

        var result = await runtime.ExecuteAsync(
            CreateStopCommand("activity"),
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Equal("STOPPED", result.ResultCode);
        Assert.DoesNotContain(
            runner.Calls,
            call => call.Arguments.Contains("-Interrupt"));
    }

    [Fact]
    public async Task Stop_InterruptsUnresponsiveManagedConsole()
    {
        var interrupted = false;
        var runner = new RecordingProcessRunner((_, arguments) =>
        {
            if (arguments.Contains("-File"))
            {
                if (arguments.Contains("-Interrupt"))
                {
                    interrupted = true;
                }

                return new ProcessRunResult(0, "ok", string.Empty);
            }

            return new ProcessRunResult(
                0,
                interrupted ? string.Empty : "1234",
                string.Empty);
        });
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            saveFlushDelay: TimeSpan.Zero,
            stopCommandGracePeriod: TimeSpan.Zero);

        var result = await runtime.ExecuteAsync(
            CreateStopCommand("activity"),
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Equal("STOPPED_WITH_INTERRUPT", result.ResultCode);
        Assert.Contains(
            runner.Calls,
            call => call.Arguments.Contains("-Interrupt"));
    }

    [Fact]
    public async Task Stop_RejectsInterruptWhenListeningProcessChanges()
    {
        var probeCount = 0;
        var runner = new RecordingProcessRunner((_, arguments) =>
        {
            if (arguments.Contains("-File"))
            {
                return new ProcessRunResult(0, "ok", string.Empty);
            }

            probeCount++;
            return new ProcessRunResult(
                0,
                probeCount == 1 ? "1234" : "5678",
                string.Empty);
        });
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            saveFlushDelay: TimeSpan.Zero,
            stopCommandGracePeriod: TimeSpan.Zero);

        var result = await runtime.ExecuteAsync(
            CreateStopCommand("activity"),
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("STOP_TARGET_CHANGED", result.ResultCode);
        Assert.DoesNotContain(
            runner.Calls,
            call => call.Arguments.Contains("-Interrupt"));
    }

    [Fact]
    public async Task Stop_ReportsInterruptBridgeFailure()
    {
        var runner = new RecordingProcessRunner((_, arguments) =>
        {
            if (arguments.Contains("-Interrupt"))
            {
                return new ProcessRunResult(1, string.Empty, "bridge failed");
            }

            if (arguments.Contains("-File"))
            {
                return new ProcessRunResult(0, "ok", string.Empty);
            }

            return new ProcessRunResult(0, "1234", string.Empty);
        });
        var runtime = CreateRuntime(
            "activity",
            25568,
            null,
            runner,
            saveFlushDelay: TimeSpan.Zero,
            stopCommandGracePeriod: TimeSpan.Zero);

        var result = await runtime.ExecuteAsync(
            CreateStopCommand("activity"),
            [runtime],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("STOP_INTERRUPT_FAILED", result.ResultCode);
        Assert.Contains("bridge failed", result.ResultMessage);
    }

    [Fact]
    public async Task Start_RejectsLocallyActiveConflictBeforeTaskExecution()
    {
        var runner = new RecordingProcessRunner((executable, arguments) =>
        {
            if (executable == "pwsh.exe")
            {
                var script = arguments.Last();
                return script.Contains("25566", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, string.Empty, string.Empty)
                    : new ProcessRunResult(0, "4321", string.Empty);
            }

            return new ProcessRunResult(0, string.Empty, string.Empty);
        });
        var target = CreateRuntime("fanstreet", 25566, "event-slot", runner);
        var conflict = CreateRuntime("yugong", 25567, "event-slot", runner);
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "fanstreet",
            ServerControlCommandKind.Start,
            1,
            null,
            null);

        var result = await target.ExecuteAsync(
            command,
            [target, conflict],
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Conflict, result.Outcome);
        Assert.Equal("LOCAL_CONFLICT_ACTIVE", result.ResultCode);
        Assert.DoesNotContain(
            runner.Calls,
            call => call.Executable == "schtasks.exe");
    }

    [Fact]
    public async Task Start_SharedPortWithoutManagedOwnerFailsClosed()
    {
        var runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            "hechao-agent-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeDirectory);
        try
        {
            var runner = new RecordingProcessRunner((executable, arguments) =>
                executable == "pwsh.exe" &&
                arguments.Last().Contains(
                    "Get-NetTCPConnection",
                    StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "9911", string.Empty)
                    : new ProcessRunResult(0, string.Empty, string.Empty));
            var target = CreateRuntime(
                "fanstreet",
                25565,
                "event-slot",
                runner,
                requiresManagedMarker: true,
                runtimeMarkerDirectory: runtimeDirectory);
            var conflict = CreateRuntime(
                "yugong",
                25565,
                "event-slot",
                runner,
                requiresManagedMarker: true,
                runtimeMarkerDirectory: runtimeDirectory);
            var command = new ServerControlCommandDelivery(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "fanstreet",
                ServerControlCommandKind.Start,
                1,
                null,
                null);

            var result = await target.ExecuteAsync(
                command,
                [target, conflict],
                CancellationToken.None);

            Assert.Equal(ServerControlCommandOutcome.Conflict, result.Outcome);
            Assert.Equal("LOCAL_PORT_OCCUPIED", result.ResultCode);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Executable == "schtasks.exe");
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task OfflinePortProbe_ReturnsNoProcessWithoutFailingHeartbeat()
    {
        var runner = new RecordingProcessRunner((_, arguments) =>
        {
            var script = arguments.Last();
            return script.EndsWith("exit 0", StringComparison.Ordinal)
                ? new ProcessRunResult(0, string.Empty, string.Empty)
                : new ProcessRunResult(1, string.Empty, string.Empty);
        });
        var runtime = CreateRuntime("survival1", 19228, null, runner);

        var processId = await runtime.FindProcessIdAsync(
            CancellationToken.None);

        Assert.Null(processId);
        Assert.Contains(
            "exit 0",
            Assert.Single(runner.Calls).Arguments.Last(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeartbeatReportsExactDeployedPackageIdentity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-agent-deployment-heartbeat-" + Guid.NewGuid().ToString("N"));
        var serverDirectory = Path.Combine(root, "activity");
        Directory.CreateDirectory(serverDirectory);
        var importId = Guid.NewGuid();
        await File.WriteAllTextAsync(
            Path.Combine(serverDirectory, ".hechao-deployment.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                importId,
                profileId = "summer-neoforge-1.21.11",
                version = "1.2.3",
                archiveSha256 = new string('a', 64),
                deployedAt = DateTimeOffset.UtcNow
            }));
        var runner = new RecordingProcessRunner((_, _) =>
            new ProcessRunResult(0, string.Empty, string.Empty));
        var runtime = CreateRuntime(
            "activity",
            25568,
            "owl5-activity-slot",
            runner,
            serverDirectory: serverDirectory,
            packageDeploymentEnabled: true,
            runtimeMarkerDirectory: Path.Combine(root, "runtime"));

        try
        {
            var heartbeat = await runtime.CaptureHeartbeatAsync(
                CancellationToken.None);

            Assert.Equal(
                new ServerPackageDeploymentIdentity(
                    importId,
                    "summer-neoforge-1.21.11",
                    "1.2.3"),
                heartbeat.DeployedPackage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SharedPortHeartbeat_RequiresMatchingManagedRunMarker()
    {
        var runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            "hechao-agent-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeDirectory);
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            File.WriteAllText(
                Path.Combine(runtimeDirectory, "yugong.json"),
                JsonSerializer.Serialize(new
                {
                    serverId = "yugong",
                    runnerProcessId = current.Id,
                    runnerStartedAtUtcTicks =
                        current.StartTime.ToUniversalTime().Ticks,
                    serverDirectory = @"C:\servers\yugong"
                }));
            var runner = new RecordingProcessRunner((executable, arguments) =>
            {
                Assert.Equal("pwsh.exe", executable);
                Assert.Contains(
                    current.Id.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    arguments.Last(),
                    StringComparison.Ordinal);
                return new ProcessRunResult(0, "4321", string.Empty);
            });
            var runtime = CreateRuntime(
                "yugong",
                25565,
                "event-slot",
                runner,
                requiresManagedMarker: true,
                runtimeMarkerDirectory: runtimeDirectory);

            var processId = await runtime.FindProcessIdAsync(
                CancellationToken.None);

            Assert.Equal(4321, processId);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    private static ServerTargetRuntime CreateRuntime(
        string serverId,
        int port,
        string? conflictGroup,
        IProcessRunner runner,
        IReadOnlyList<string>? prefixes = null,
        bool requiresManagedMarker = false,
        string? runtimeMarkerDirectory = null,
        TimeSpan? saveFlushDelay = null,
        TimeSpan? stopCommandGracePeriod = null,
        string? serverDirectory = null,
        bool serverDeletionEnabled = false,
        bool packageDeploymentEnabled = false,
        IReadOnlyList<string>? hostManagedRelativePaths = null,
        string? backupRoot = null)
    {
        var configuration = new ServerControlTargetConfiguration
        {
            ServerId = serverId,
            ServerDirectory = serverDirectory ?? $@"C:\servers\{serverId}",
            StartTaskName = $"Hechao-Server-{serverId}",
            Port = port,
            ConflictGroup = conflictGroup,
            AllowedCommandPrefixes = prefixes ?? ["list", "say", "save-all"],
            ServerDeletionEnabled = serverDeletionEnabled,
            PackageDeploymentEnabled = packageDeploymentEnabled,
            HostManagedRelativePaths = hostManagedRelativePaths ?? []
        };
        return new ServerTargetRuntime(
            configuration,
            @"C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1",
            backupRoot ?? @"C:\ProgramData\Hechao\ServerControlAgent\backups",
            runtimeMarkerDirectory ??
                @"C:\ProgramData\Hechao\ServerControlAgent\runtime",
            requiresManagedMarker,
            runner,
            saveFlushDelay,
            stopCommandGracePeriod);
    }

    private static ServerControlCommandDelivery CreateStopCommand(
        string serverId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            serverId,
            ServerControlCommandKind.Stop,
            1,
            null,
            null);

    private sealed class RecordingProcessRunner(
        Func<string, IReadOnlyList<string>, ProcessRunResult> response)
        : IProcessRunner
    {
        internal List<(string Executable, IReadOnlyList<string> Arguments)> Calls
        {
            get;
        } = [];

        public Task<ProcessRunResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((executable, arguments.ToArray()));
            return Task.FromResult(response(executable, arguments));
        }
    }
}
