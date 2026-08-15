namespace Hechao.ServerControlAgent.Tests;

public sealed class AgentConfigurationTests
{
    [Fact]
    public void Validate_AcceptsTargetsSharingDeclaredConflictGroup()
    {
        var configuration = CreateConfiguration(
            CreateTarget("horror-prank", @"C:\mc\server", 25565, "owl9-25565"),
            CreateTarget("pvp-purpur", @"E:\MinecraftServer", 25565, "owl9-25565"));

        configuration.Validate();
    }

    [Fact]
    public void Validate_RejectsSharedPortWithoutConflictGroup()
    {
        var configuration = CreateConfiguration(
            CreateTarget("horror-prank", @"C:\mc\server", 25565, null),
            CreateTarget("pvp-purpur", @"E:\MinecraftServer", 25565, null));

        Assert.Throws<InvalidDataException>(configuration.Validate);
    }

    [Fact]
    public void Validate_RejectsUnapprovedScheduledTaskName()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            null) with
        {
            StartTaskName = "Other-Task"
        };

        Assert.Throws<InvalidDataException>(target.Validate);
    }

    [Fact]
    public void Validate_RejectsRelativePathTraversal()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            null) with
        {
            LogRelativePath = @"..\other\latest.log"
        };

        Assert.Throws<InvalidDataException>(target.Validate);
    }

    [Fact]
    public void Validate_RejectsUnsafeMemorySettingsConfiguration()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            null) with
        {
            MemorySettingsRelativePath = @"..\other\start.bat",
            MaximumAllowedMemoryMiB = 256
        };

        Assert.Throws<InvalidDataException>(target.Validate);
    }

    [Theory]
    [InlineData("server-control-agent.owl5.production.json", "owl5", 7)]
    [InlineData("server-control-agent.owl9.production.json", "owl9", 2)]
    public void Load_AcceptsProductionInventory(
        string fileName,
        string expectedAgentId,
        int expectedTargetCount)
    {
        var configuration = ServerControlAgentConfiguration.Load(
            Path.Combine(AppContext.BaseDirectory, fileName));

        Assert.Equal(expectedAgentId, configuration.AgentId);
        Assert.Equal(expectedTargetCount, configuration.Targets.Count);
        Assert.All(configuration.Targets, target =>
        {
            Assert.False(string.IsNullOrWhiteSpace(target.MemorySettingsRelativePath));
            Assert.InRange(target.MaximumAllowedMemoryMiB, 512, 65536);
        });
        if (expectedAgentId == "owl5")
        {
            Assert.Equal(
                ["activity", "dollnight", "fanstreet", "yugong"],
                configuration.Targets
                    .Where(target => target.ServerDeletionEnabled)
                    .Select(target => target.ServerId)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            var deploymentTargets = configuration.Targets
                .Where(target => target.PackageDeploymentEnabled)
                .ToArray();
            var activity = Assert.Single(deploymentTargets);
            Assert.Equal("activity", activity.ServerId);
            Assert.Equal("start.bat", activity.StartScriptRelativePath);
            Assert.Equal(["forwarding.secret"], activity.HostManagedRelativePaths);
            Assert.Equal(
                ["world", "world_nether", "world_the_end"],
                activity.WorldDataRelativePaths);
        }
        else
        {
            Assert.Equal(
                ["pvp"],
                configuration.Targets
                    .Where(target => target.ServerDeletionEnabled)
                    .Select(target => target.ServerId)
                    .ToArray());
            Assert.DoesNotContain(
                configuration.Targets,
                target => target.PackageDeploymentEnabled);
        }
    }

    [Fact]
    public void Validate_RejectsDeploymentPathsWhenCapabilityIsDisabled()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            "owl5-activity-slot") with
        {
            HostManagedRelativePaths = ["forwarding.secret"]
        };

        Assert.Throws<InvalidDataException>(target.Validate);
    }

    [Fact]
    public void Validate_AcceptsPackageDeploymentOnOwl5ActivitySlots()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            "owl5-activity-slot") with
        {
            PackageDeploymentEnabled = true,
            HostManagedRelativePaths = ["forwarding.secret"],
            WorldDataRelativePaths = ["world", "world_nether", "world_the_end"]
        };
        var configuration = CreateConfiguration(target) with { AgentId = "owl5" };

        configuration.Validate();

        var sibling = target with
        {
            ServerId = "activity-ready-check",
            ServerDirectory = @"E:\ActivityReadyCheck",
            StartTaskName = "Hechao-Server-ActivityReadyCheck"
        };
        (configuration with { Targets = [target, sibling] }).Validate();
    }

    [Fact]
    public void Validate_RejectsPackageDeploymentOutsideOwl5ActivitySlot()
    {
        var approvedTarget = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            "owl5-activity-slot") with
        {
            PackageDeploymentEnabled = true,
            HostManagedRelativePaths = ["forwarding.secret"]
        };
        var invalidConfigurations = new[]
        {
            CreateConfiguration(approvedTarget),
            CreateConfiguration(approvedTarget with { Port = 25569 })
                with { AgentId = "owl5" },
            CreateConfiguration(approvedTarget with { ConflictGroup = "other-slot" })
                with { AgentId = "owl5" }
        };

        Assert.All(invalidConfigurations, configuration =>
            Assert.Throws<InvalidDataException>(configuration.Validate));
    }

    [Fact]
    public void Validate_RejectsOverlappingOrProtectedPreservedPaths()
    {
        var target = CreateTarget(
            "activity",
            @"E:\ActivityNeoForge",
            25568,
            "owl5-activity-slot") with
        {
            PackageDeploymentEnabled = true
        };
        var invalidTargets = new[]
        {
            target with
            {
                HostManagedRelativePaths = ["config", @"config\forwarding.secret"]
            },
            target with
            {
                HostManagedRelativePaths = ["server.properties"]
            },
            target with
            {
                HostManagedRelativePaths = ["scripts"],
                StartScriptRelativePath = @"scripts\start.bat"
            },
            target with
            {
                HostManagedRelativePaths = ["forwarding.secret"],
                WorldDataRelativePaths = ["forwarding.secret"]
            }
        };

        Assert.All(invalidTargets, invalidTarget =>
            Assert.Throws<InvalidDataException>(invalidTarget.Validate));
    }

    [Fact]
    public void Validate_RejectsDeletionRootContainingAnotherManagedServer()
    {
        var parent = CreateTarget(
            "activity-root",
            @"E:\Activities",
            25568,
            null) with
        {
            ServerDeletionEnabled = true
        };
        var child = CreateTarget(
            "activity-child",
            @"E:\Activities\Current",
            25569,
            null);
        var configuration = CreateConfiguration(parent, child);

        Assert.Throws<InvalidDataException>(configuration.Validate);
    }

    [Fact]
    public void Validate_RejectsDeletionRootInsideAnotherManagedServer()
    {
        var parent = CreateTarget(
            "activity-root",
            @"E:\Activities",
            25568,
            null);
        var child = CreateTarget(
            "activity-child",
            @"E:\Activities\Current",
            25569,
            null) with
        {
            ServerDeletionEnabled = true
        };
        var configuration = CreateConfiguration(parent, child);

        Assert.Throws<InvalidDataException>(configuration.Validate);
    }

    [Fact]
    public void Validate_RejectsVolumeRootDeletionTarget()
    {
        var target = CreateTarget(
            "activity",
            @"E:\",
            25568,
            null) with
        {
            ServerDeletionEnabled = true
        };

        Assert.Throws<InvalidDataException>(target.Validate);
    }

    private static ServerControlAgentConfiguration CreateConfiguration(
        params ServerControlTargetConfiguration[] targets) =>
        new()
        {
            ApiBaseUrl = "https://launcher-api.hechao.world",
            AgentId = "owl9",
            TokenPath = @"C:\ProgramData\Hechao\ServerControlAgent\token.dat",
            StateDirectory = @"C:\ProgramData\Hechao\ServerControlAgent",
            ConsoleSubmitScript =
                @"C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1",
            Targets = targets
        };

    private static ServerControlTargetConfiguration CreateTarget(
        string serverId,
        string directory,
        int port,
        string? conflictGroup) =>
        new()
        {
            ServerId = serverId,
            ServerDirectory = directory,
            StartTaskName = $"Hechao-Server-{serverId}",
            Port = port,
            ConflictGroup = conflictGroup,
            AllowedCommandPrefixes = ["list", "say", "save-all"]
        };
}
