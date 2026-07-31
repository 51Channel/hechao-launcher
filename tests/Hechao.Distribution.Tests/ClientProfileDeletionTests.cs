using System.Net.Http;

namespace Hechao.Distribution.Tests;

public sealed class ClientProfileDeletionTests
{
    [Fact]
    public async Task DeleteAsync_RemovesProfileRollbackAndStagingButKeepsSharedData()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();

        var profileRoot = layout.GetProfileRoot("activity-test");
        var previousRoot = layout.GetPreviousProfileRoot("activity-test");
        var stagingRoot = Path.Combine(
            layout.InstancesRoot,
            ".activity-test.staging-1234567890abcdef");
        Directory.CreateDirectory(Path.Combine(profileRoot, ".minecraft", "mods"));
        Directory.CreateDirectory(Path.Combine(previousRoot, ".minecraft"));
        Directory.CreateDirectory(stagingRoot);
        var readOnlyFile = Path.Combine(profileRoot, ".minecraft", "mods", "old.jar");
        await File.WriteAllTextAsync(readOnlyFile, "old-client");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
        await File.WriteAllTextAsync(
            layout.PlayerOptionsPath,
            "mouseSensitivity:0.5");
        var sharedObject = Path.Combine(layout.ObjectCacheRoot, "shared.bin");
        await File.WriteAllTextAsync(sharedObject, "shared");

        var deleted = await CreateInstaller().DeleteAsync(
            temporary.Path,
            "activity-test");

        Assert.True(deleted);
        Assert.False(Directory.Exists(profileRoot));
        Assert.False(Directory.Exists(previousRoot));
        Assert.False(Directory.Exists(stagingRoot));
        Assert.True(File.Exists(layout.PlayerOptionsPath));
        Assert.True(File.Exists(sharedObject));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenProfileIsAlreadyMissing()
    {
        using var temporary = new TemporaryDirectory();

        var deleted = await CreateInstaller().DeleteAsync(
            temporary.Path,
            "activity-test");

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsConcurrentProfileMutation()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        Directory.CreateDirectory(layout.GetProfileRoot("activity-test"));
        await using var installationLock = new FileStream(
            Path.Combine(layout.LocksRoot, "activity-test.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        await Assert.ThrowsAsync<ProfileInstallInProgressException>(() =>
            CreateInstaller().DeleteAsync(temporary.Path, "activity-test"));

        Assert.True(Directory.Exists(layout.GetProfileRoot("activity-test")));
    }

    private static ClientProfileInstaller CreateInstaller() =>
        new(new ResumableFileDownloader(new HttpClient()));
}
