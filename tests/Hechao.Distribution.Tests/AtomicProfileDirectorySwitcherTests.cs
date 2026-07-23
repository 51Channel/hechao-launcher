namespace Hechao.Distribution.Tests;

public sealed class AtomicProfileDirectorySwitcherTests
{
    [Fact]
    public void Switch_RestoresActiveAndPreviousDirectoriesWhenActivationFails()
    {
        using var temporary = new TemporaryDirectory();
        var active = CreateDirectoryWithMarker(temporary.Path, "active", "active-v1");
        var previous = CreateDirectoryWithMarker(temporary.Path, "previous", "active-v0");
        var staging = CreateDirectoryWithMarker(temporary.Path, "staging", "active-v2");
        var switcher = new AtomicProfileDirectorySwitcher(
            () => throw new IOException("Simulated antivirus file lock."));

        Assert.Throws<IOException>(() => switcher.Switch(staging, active, previous));

        Assert.Equal("active-v1", File.ReadAllText(Path.Combine(active, "marker.txt")));
        Assert.Equal("active-v0", File.ReadAllText(Path.Combine(previous, "marker.txt")));
        Assert.Equal("active-v2", File.ReadAllText(Path.Combine(staging, "marker.txt")));
    }

    [Fact]
    public void Switch_ActivatesStagingAndRetainsOnePreviousVersion()
    {
        using var temporary = new TemporaryDirectory();
        var active = CreateDirectoryWithMarker(temporary.Path, "active", "active-v1");
        var previous = CreateDirectoryWithMarker(temporary.Path, "previous", "active-v0");
        var staging = CreateDirectoryWithMarker(temporary.Path, "staging", "active-v2");
        var switcher = new AtomicProfileDirectorySwitcher();

        switcher.Switch(staging, active, previous);

        Assert.Equal("active-v2", File.ReadAllText(Path.Combine(active, "marker.txt")));
        Assert.Equal("active-v1", File.ReadAllText(Path.Combine(previous, "marker.txt")));
        Assert.False(Directory.Exists(staging));
    }

    private static string CreateDirectoryWithMarker(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "marker.txt"), content);
        return path;
    }
}
