using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class DynamicDeploymentSlotProvisionerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-dynamic-slot-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProvisionAsync_CreatesStoppedSlotAndSupportsIdempotentReplay()
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var request = CreateRequest("activity-summer");

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Equal("SLOT_PROVISIONED", result.ResultCode);
        var targetDirectory = Path.Combine(fixture.SlotRoot, request.ServerId);
        Assert.Equal(
            "forwarding-secret",
            await File.ReadAllTextAsync(
                Path.Combine(targetDirectory, "forwarding.secret")));
        Assert.Contains(
            "if not defined HECHAO_MANAGED_START pause",
            await File.ReadAllTextAsync(
                Path.Combine(targetDirectory, "start.bat")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "server-port=25600",
            await File.ReadAllTextAsync(
                Path.Combine(targetDirectory, "server.properties")),
            StringComparison.Ordinal);
        Assert.Equal(2, fixture.Registry.Snapshot().Count);
        var dynamicTarget = Assert.Single(fixture.Store.Snapshot());
        Assert.Equal(request.ServerId, dynamicTarget.ServerId);
        Assert.True(dynamicTarget.RequireDeployedPackage);
        Assert.True(dynamicTarget.PackageDeploymentEnabled);
        Assert.True(dynamicTarget.ServerDeletionEnabled);
        Assert.Equal(25600, dynamicTarget.Port);
        Assert.Null(dynamicTarget.ConflictGroup);

        var callCount = fixture.Runner.Calls.Count;
        var replay = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, replay.Outcome);
        Assert.Equal("SLOT_ALREADY_PROVISIONED", replay.ResultCode);
        Assert.Equal(callCount, fixture.Runner.Calls.Count);
        Assert.DoesNotContain(
            fixture.Runner.Calls,
            call => call.Arguments.Contains("/Run", StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("activity-summer", DeploymentSlotKind.Activity)]
    [InlineData("survival-industry", DeploymentSlotKind.Survival)]
    [InlineData("pvp-ranked", DeploymentSlotKind.Pvp)]
    [InlineData("minigame-party", DeploymentSlotKind.Minigame)]
    public async Task ProvisionAsync_AcceptsEverySlotFamily(
        string serverId,
        DeploymentSlotKind slotKind)
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var request = CreateRequest(serverId, slotKind: slotKind);

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        var target = Assert.Single(fixture.Store.Snapshot());
        Assert.Equal(request.Port, target.Port);
        Assert.Null(target.ConflictGroup);
    }

    [Fact]
    public async Task ProvisionAsync_RejectsKindPrefixMismatchWithoutCreatingFiles()
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var request = CreateRequest(
            "activity-ranked",
            slotKind: DeploymentSlotKind.Pvp);

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("INVALID_SLOT_PROVISIONING", result.ResultCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.SlotRoot, request.ServerId)));
        Assert.Empty(fixture.Store.Snapshot());
        Assert.Empty(fixture.Runner.Calls);
    }

    [Fact]
    public async Task ProvisionAsync_RejectsAllocatedPortConflictWithoutCreatingFiles()
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var first = CreateRequest("activity-first");
        var firstResult = await fixture.Provisioner.ProvisionAsync(
            first,
            CancellationToken.None);
        Assert.Equal(ServerControlCommandOutcome.Succeeded, firstResult.Outcome);

        var second = CreateRequest(
            "survival-second",
            port: first.Port,
            slotKind: DeploymentSlotKind.Survival);
        var result = await fixture.Provisioner.ProvisionAsync(
            second,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("SLOT_PORT_CONFLICT", result.ResultCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.SlotRoot, second.ServerId)));
        Assert.Single(fixture.Store.Snapshot());
    }

    [Fact]
    public async Task ProvisionAsync_PersistsTargetForAgentReload()
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var request = CreateRequest("activity-reload");
        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);
        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);

        var reloaded = new DynamicDeploymentSlotStore(fixture.Configuration);
        var target = Assert.Single(reloaded.Snapshot());

        Assert.Equal(request.ServerId, target.ServerId);
        Assert.Equal(
            Path.Combine(fixture.SlotRoot, request.ServerId),
            target.ServerDirectory,
            ignoreCase: true);
        fixture.Configuration.ValidateDynamicTargets(reloaded.Snapshot());
    }

    [Fact]
    public async Task ProvisionAsync_RollsBackDirectoryTaskSnapshotAndStoreOnInstallerFailure()
    {
        var runner = new RecordingProcessRunner((call, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (call.Executable.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ProcessRunResult(1, "", "installer failed"));
            }

            return Task.FromResult(call.Arguments.Contains("/Query")
                ? new ProcessRunResult(1, "", "not found")
                : new ProcessRunResult(0, "", ""));
        });
        var fixture = CreateFixture(runner);
        var request = CreateRequest("activity-install-failure");

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("SLOT_PROVISIONING_FAILED", result.ResultCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.SlotRoot, request.ServerId)));
        Assert.False(Directory.Exists(Path.Combine(
            fixture.BackupRoot,
            "host-managed",
            request.ServerId)));
        Assert.Empty(fixture.Store.Snapshot());
        Assert.Single(fixture.Registry.Snapshot());
        Assert.Contains(
            runner.Calls,
            call => call.Arguments.Contains("/Delete") &&
                    call.Arguments.Contains(
                        "Hechao-Server-" + request.ServerId,
                        StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProvisionAsync_RollsBackPartialResourcesWhenAgentStops()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RecordingProcessRunner((call, cancellationToken) =>
        {
            if (call.Executable.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
            {
                cancellation.Cancel();
                return Task.FromCanceled<ProcessRunResult>(cancellation.Token);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(call.Arguments.Contains("/Query")
                ? new ProcessRunResult(1, "", "not found")
                : new ProcessRunResult(0, "", ""));
        });
        var fixture = CreateFixture(runner);
        var request = CreateRequest("activity-cancelled");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Provisioner.ProvisionAsync(request, cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(fixture.SlotRoot, request.ServerId)));
        Assert.False(Directory.Exists(Path.Combine(
            fixture.BackupRoot,
            "host-managed",
            request.ServerId)));
        Assert.Empty(fixture.Store.Snapshot());
        Assert.Single(fixture.Registry.Snapshot());
    }

    [Fact]
    public async Task ProvisionAsync_RefusesExistingDirectoryWithoutChangingIt()
    {
        var fixture = CreateFixture(SuccessfulProcessRunner());
        var request = CreateRequest("activity-existing-directory");
        var directory = Path.Combine(fixture.SlotRoot, request.ServerId);
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "unchanged");

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("SLOT_DIRECTORY_EXISTS", result.ResultCode);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(fixture.Runner.Calls);
        Assert.Empty(fixture.Store.Snapshot());
    }

    [Fact]
    public async Task ProvisionAsync_RefusesExistingTaskWithoutCreatingDirectory()
    {
        var runner = new RecordingProcessRunner((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProcessRunResult(0, "task exists", ""));
        });
        var fixture = CreateFixture(runner);
        var request = CreateRequest("activity-existing-task");

        var result = await fixture.Provisioner.ProvisionAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("SLOT_TASK_EXISTS", result.ResultCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.SlotRoot, request.ServerId)));
        Assert.Single(runner.Calls);
        Assert.Empty(fixture.Store.Snapshot());
    }

    private Fixture CreateFixture(RecordingProcessRunner runner)
    {
        var stateDirectory = Path.Combine(root, "state");
        var slotRoot = Path.Combine(root, "slots");
        var templateDirectory = Path.Combine(root, "template");
        var backupRoot = Path.Combine(root, "backups");
        var runtimeDirectory = Path.Combine(stateDirectory, "runtime");
        Directory.CreateDirectory(templateDirectory);
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllText(
            Path.Combine(templateDirectory, "forwarding.secret"),
            "forwarding-secret");
        File.WriteAllText(
            Path.Combine(templateDirectory, "server.properties"),
            "server-port=25568\r\n");
        File.WriteAllText(
            Path.Combine(templateDirectory, "user_jvm_args.txt"),
            "-Xms1024M\r\n-Xmx4096M\r\n");
        File.WriteAllText(
            Path.Combine(templateDirectory, "start.bat"),
            "@echo off\r\nif not defined HECHAO_MANAGED_START pause\r\n");
        var template = new ServerControlTargetConfiguration
        {
            ServerId = "activity",
            ServerDirectory = templateDirectory,
            StartTaskName = "Hechao-Server-activity",
            Port = 25568,
            ConflictGroup = "owl5-activity-slot",
            PropertiesRelativePath = "server.properties",
            MemorySettingsRelativePath = "user_jvm_args.txt",
            StartScriptRelativePath = "start.bat",
            MaximumAllowedMemoryMiB = 8192,
            PackageDeploymentEnabled = true,
            ServerDeletionEnabled = true,
            HostManagedRelativePaths = ["forwarding.secret"],
            WorldDataRelativePaths = ["world", "world_nether", "world_the_end"],
            AllowedCommandPrefixes = ["list", "say", "save-all"]
        }.Normalize();
        var configuration = new ServerControlAgentConfiguration
        {
            ApiBaseUrl = "https://launcher-api.hechao.world",
            AgentId = "owl5",
            TokenPath = Path.Combine(stateDirectory, "token.dat"),
            StateDirectory = stateDirectory,
            ConsoleSubmitScript = Path.Combine(root, "Submit-MinecraftConsoleCommand.ps1"),
            DeploymentSlotProvisioning = new DeploymentSlotProvisioningConfiguration
            {
                Enabled = true,
                RootDirectory = slotRoot,
                TemplateServerId = "activity",
                TaskInstallerScript = Path.Combine(root, "Install-MinecraftServerLaunchTask.ps1"),
                MaximumSlots = 12
            },
            Targets = [template]
        };
        configuration.Validate();
        new HostManagedSnapshotStore(template, backupRoot).CaptureFromServer();
        var store = new DynamicDeploymentSlotStore(configuration);

        ServerTargetRuntime CreateRuntime(ServerControlTargetConfiguration target) =>
            new(
                target,
                configuration.ConsoleSubmitScript,
                backupRoot,
                runtimeDirectory,
                requiresManagedMarker: true,
                runner,
                saveFlushDelay: TimeSpan.Zero,
                stopCommandGracePeriod: TimeSpan.FromMilliseconds(1));

        var registry = new ServerTargetRegistry([CreateRuntime(template)]);
        var provisioner = new DynamicDeploymentSlotProvisioner(
            configuration,
            store,
            registry,
            CreateRuntime,
            runner,
            backupRoot,
            runtimeDirectory);
        return new Fixture(
            configuration,
            slotRoot,
            backupRoot,
            store,
            registry,
            provisioner,
            runner);
    }

    private static RecordingProcessRunner SuccessfulProcessRunner() =>
        new((call, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                call.Arguments.Contains("/Query")
                    ? new ProcessRunResult(1, "", "not found")
                    : new ProcessRunResult(0, "installed", ""));
        });

    private static ServerDeploymentSlotProvisioningRequest CreateRequest(
        string serverId,
        int port = 25600,
        DeploymentSlotKind slotKind = DeploymentSlotKind.Activity) =>
        new(serverId, "测试部署槽", "activity", port, slotKind);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record Fixture(
        ServerControlAgentConfiguration Configuration,
        string SlotRoot,
        string BackupRoot,
        DynamicDeploymentSlotStore Store,
        ServerTargetRegistry Registry,
        DynamicDeploymentSlotProvisioner Provisioner,
        RecordingProcessRunner Runner);

    private sealed record ProcessCall(
        string Executable,
        IReadOnlyList<string> Arguments);

    private sealed class RecordingProcessRunner(
        Func<ProcessCall, CancellationToken, Task<ProcessRunResult>> handler)
        : IProcessRunner
    {
        internal List<ProcessCall> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var call = new ProcessCall(executable, arguments.ToArray());
            Calls.Add(call);
            return handler(call, cancellationToken);
        }
    }
}
