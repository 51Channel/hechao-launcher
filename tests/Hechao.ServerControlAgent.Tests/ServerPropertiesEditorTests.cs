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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
