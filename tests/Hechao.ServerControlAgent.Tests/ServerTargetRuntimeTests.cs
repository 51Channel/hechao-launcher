using Hechao.Contracts;
using System.Text.Json;

namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerTargetRuntimeTests
{
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
            ["list", "stop"]);
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
        string? runtimeMarkerDirectory = null)
    {
        var configuration = new ServerControlTargetConfiguration
        {
            ServerId = serverId,
            ServerDirectory = $@"C:\servers\{serverId}",
            StartTaskName = $"Hechao-Server-{serverId}",
            Port = port,
            ConflictGroup = conflictGroup,
            AllowedCommandPrefixes = prefixes ?? ["list", "say", "save-all"]
        };
        return new ServerTargetRuntime(
            configuration,
            @"C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1",
            @"C:\ProgramData\Hechao\ServerControlAgent\backups",
            runtimeMarkerDirectory ??
                @"C:\ProgramData\Hechao\ServerControlAgent\runtime",
            requiresManagedMarker,
            runner);
    }

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
