namespace Hechao.Distribution.Tests;

public sealed class ClientStorageLayoutTests
{
    [Fact]
    public void Layout_SeparatesProfilesSharedFilesAndInternalState()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);

        Assert.Equal(
            Path.Combine(temporary.Path, "instances"),
            layout.InstancesRoot);
        Assert.Equal(
            Path.Combine(temporary.Path, "shared", "objects"),
            layout.ObjectCacheRoot);
        Assert.Equal(
            Path.Combine(temporary.Path, "shared", "runtime"),
            layout.RuntimeRoot);
        Assert.Equal(
            Path.Combine(temporary.Path, "shared", "player-settings"),
            layout.PlayerSettingsRoot);
        Assert.Equal(
            Path.Combine(temporary.Path, "shared", "player-settings", "options.txt"),
            layout.PlayerOptionsPath);
        Assert.Equal(
            Path.Combine(
                temporary.Path,
                "instances",
                "base-1.21.11",
                "runtime"),
            layout.GetProfileRuntimeRoot("base-1.21.11"));
        Assert.Equal(
            Path.Combine(
                temporary.Path,
                "instances",
                "base-1.21.11",
                ClientStorageLayout.GameDirectoryName),
            layout.GetProfileGameDirectory("base-1.21.11"));
    }

    [Fact]
    public void Layout_RejectsUnsafeProfileIdentifier()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);

        Assert.Throws<ManifestFormatException>(
            () => layout.GetProfileRoot("../outside"));
    }
}
