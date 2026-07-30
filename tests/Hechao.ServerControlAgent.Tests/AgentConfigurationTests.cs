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
