using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerPropertiesEditorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "hechao-control-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_UpdatesOnlyApprovedKeysAndCreatesBackup()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "server.properties");
        File.WriteAllLines(
            path,
            [
                "#Minecraft server properties",
                "online-mode=false",
                "max-players=20",
                "view-distance=10",
                "simulation-distance=10",
                "difficulty=normal",
                "white-list=false",
                "motd=Hechao"
            ]);
        var backupRoot = Path.Combine(_root, "backups");
        var settings = new ServerQuickSettings(60, 12, 8, "hard", true);

        ServerPropertiesEditor.Apply(
            path,
            backupRoot,
            "activity",
            settings);

        var text = File.ReadAllText(path);
        Assert.Contains("online-mode=false", text, StringComparison.Ordinal);
        Assert.Contains("motd=Hechao", text, StringComparison.Ordinal);
        Assert.Contains("max-players=60", text, StringComparison.Ordinal);
        Assert.Contains("view-distance=12", text, StringComparison.Ordinal);
        Assert.Contains("simulation-distance=8", text, StringComparison.Ordinal);
        Assert.Contains("difficulty=hard", text, StringComparison.Ordinal);
        Assert.Contains("white-list=true", text, StringComparison.Ordinal);
        Assert.Single(
            Directory.EnumerateFiles(
                backupRoot,
                "server.properties",
                SearchOption.AllDirectories));
        Assert.Equal(settings, ServerPropertiesEditor.Read(path));
    }

    [Fact]
    public async Task ApplyDeploymentBinding_ForcesLoopbackVelocityBackend()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "deployment.properties");
        await File.WriteAllTextAsync(
            path,
            "server-ip=0.0.0.0\nserver-port=25565\nonline-mode=true\nmotd=keep\n");

        ServerPropertiesEditor.ApplyDeploymentBinding(path, 25568);

        var result = await File.ReadAllTextAsync(path);
        Assert.Contains("server-ip=127.0.0.1", result);
        Assert.Contains("server-port=25568", result);
        Assert.Contains("online-mode=false", result);
        Assert.Contains("motd=keep", result);
    }

    [Fact]
    public void Read_LegacyPropertiesUsesViewDistanceAndMapsNumericDifficulty()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "server.properties");
        File.WriteAllText(
            path,
            "max-players=24\nview-distance=10\ndifficulty=1\nwhite-list=false\n");

        var result = ServerPropertiesEditor.Read(path);

        Assert.Equal(
            new ServerQuickSettings(24, 10, 10, "easy", false),
            result);
    }

    [Fact]
    public void Apply_LegacyPropertiesPreservesSupportedRepresentation()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "server.properties");
        File.WriteAllText(
            path,
            "max-players=24\nview-distance=10\ndifficulty=1\nwhite-list=false\n");
        var backupRoot = Path.Combine(_root, "backups");
        var settings = new ServerQuickSettings(30, 12, 12, "hard", true);

        ServerPropertiesEditor.Apply(
            path,
            backupRoot,
            "legacy-forge",
            settings);

        var text = File.ReadAllText(path);
        Assert.Contains("difficulty=3", text, StringComparison.Ordinal);
        Assert.Contains("view-distance=12", text, StringComparison.Ordinal);
        Assert.DoesNotContain("simulation-distance=", text, StringComparison.Ordinal);
        Assert.Equal(settings, ServerPropertiesEditor.Read(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
