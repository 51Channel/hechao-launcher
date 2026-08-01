using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class PlayerGameSettingsServiceTests
{
    [Fact]
    public async Task ImportLatestAsync_MergesProfilesAndKeepsNewestValues()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        var firstOptions = CreateOptionsFile(
            layout,
            "base",
            "mouseSensitivity:0.4",
            "key_key.forward:key.keyboard.w",
            "resourcePacks:[\"base.zip\"]");
        var secondOptions = CreateOptionsFile(
            layout,
            "activity",
            "mouseSensitivity:0.8",
            "gamma:1.0",
            "key_key.forward:key.keyboard.up",
            "version:4189",
            "resourcePacks:[\"activity.zip\"]",
            "lastServer:127.0.0.1:25565");
        File.SetLastWriteTimeUtc(firstOptions, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(secondOptions, DateTime.UtcNow.AddMinutes(-1));

        await new PlayerGameSettingsService().ImportLatestAsync(temporary.Path);

        var shared = ReadOptions(layout.PlayerOptionsPath);
        Assert.Equal("0.8", shared["mouseSensitivity"]);
        Assert.Equal("1.0", shared["gamma"]);
        Assert.Equal("key.keyboard.up", shared["key_key.forward"]);
        Assert.DoesNotContain("version", shared.Keys);
        Assert.DoesNotContain("resourcePacks", shared.Keys);
        Assert.DoesNotContain("lastServer", shared.Keys);
    }

    [Fact]
    public async Task ApplyToProfileAsync_PreservesProfileScopedValuesAndUnknownModBindings()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await File.WriteAllLinesAsync(
            layout.PlayerOptionsPath,
            [
                "mouseSensitivity:0.8",
                "key_key.forward:key.keyboard.up",
                "key_key.jump:key.keyboard.enter",
                "key_mod.camera:key.keyboard.c"
            ]);
        var targetPath = CreateOptionsFile(
            layout,
            "activity",
            "mouseSensitivity:0.2",
            "key_key.forward:key.keyboard.w",
            "key_mod.camera:key.keyboard.v",
            "resourcePacks:[\"activity.zip\"]");

        await new PlayerGameSettingsService().ApplyToProfileAsync(
            temporary.Path,
            "activity");

        var target = ReadOptions(targetPath);
        Assert.Equal("0.8", target["mouseSensitivity"]);
        Assert.Equal("key.keyboard.up", target["key_key.forward"]);
        Assert.Equal("key.keyboard.c", target["key_mod.camera"]);
        Assert.False(target.ContainsKey("key_key.jump"));
        Assert.Equal("[\"activity.zip\"]", target["resourcePacks"]);
    }

    [Fact]
    public async Task ApplyToProfileAsync_SeedsNewProfileWithSharedBindings()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await File.WriteAllLinesAsync(
            layout.PlayerOptionsPath,
            [
                "mouseSensitivity:0.7",
                "key_key.forward:key.keyboard.up"
            ]);

        await new PlayerGameSettingsService().ApplyToProfileAsync(
            temporary.Path,
            "new-activity");

        var target = ReadOptions(Path.Combine(
            layout.GetProfileGameDirectory("new-activity"),
            "options.txt"));
        Assert.Equal("0.7", target["mouseSensitivity"]);
        Assert.Equal("key.keyboard.up", target["key_key.forward"]);
    }

    [Fact]
    public async Task CaptureProfileAsync_UpdatesSharedSettingsAfterGameExit()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await File.WriteAllTextAsync(
            layout.PlayerOptionsPath,
            "mouseSensitivity:0.4");
        File.SetLastWriteTimeUtc(
            layout.PlayerOptionsPath,
            DateTime.UtcNow.AddMinutes(-2));
        var targetPath = CreateOptionsFile(
            layout,
            "base",
            "mouseSensitivity:0.9",
            "fov:0.5");
        File.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow.AddMinutes(-1));

        await new PlayerGameSettingsService().CaptureProfileAsync(
            temporary.Path,
            "base");

        var shared = ReadOptions(layout.PlayerOptionsPath);
        Assert.Equal("0.9", shared["mouseSensitivity"]);
        Assert.Equal("0.5", shared["fov"]);
    }

    [Fact]
    public async Task SeparateInstances_SerializeConcurrentCapturesAcrossNormalizedDataRoot()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await File.WriteAllTextAsync(layout.PlayerOptionsPath, "gamma:1.0");

        var profileIds = Enumerable.Range(0, 8)
            .Select(index => $"activity-{index:00}")
            .ToArray();

        foreach (var (profileId, index) in profileIds.Select((profileId, index) => (profileId, index)))
        {
            var optionsPath = CreateOptionsFile(
                layout,
                profileId,
                $"key_concurrent.{index}:key.keyboard.{index}");
            File.SetLastWriteTimeUtc(optionsPath, DateTime.UtcNow.AddMinutes(index + 1));
        }

        var services = new[]
        {
            new PlayerGameSettingsService(),
            new PlayerGameSettingsService()
        };
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = profileIds
            .Select((profileId, index) => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                var dataRoot = index % 2 == 0
                    ? temporary.Path
                    : Path.Combine(temporary.Path, ".");
                await services[index % services.Length]
                    .CaptureProfileAsync(dataRoot, profileId)
                    .ConfigureAwait(false);
            }))
            .ToArray();

        start.SetResult(true);
        await Task.WhenAll(captures);

        var shared = ReadOptions(layout.PlayerOptionsPath);
        foreach (var (profileId, index) in profileIds.Select((profileId, index) => (profileId, index)))
        {
            Assert.Equal($"key.keyboard.{index}", shared[$"key_concurrent.{index}"]);
        }

        Assert.Empty(Directory.EnumerateFiles(layout.PlayerSettingsRoot, "*.tmp"));
    }

    [Fact]
    public async Task HeldLock_TimesOutAndRecoversAfterOwnerHandleCloses()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await File.WriteAllTextAsync(layout.PlayerOptionsPath, "mouseSensitivity:0.8");
        var lockPath = PlayerGameSettingsService.GetLockPath(temporary.Path);
        var contendingService = new PlayerGameSettingsService(TimeSpan.FromMilliseconds(100));

        using (PathFileLock.Acquire(
                   layout.DataRoot,
                   lockPath,
                   TimeSpan.FromSeconds(1)))
        {
            var waitStartedAt = DateTime.UtcNow;
            var exception = await Assert.ThrowsAsync<PlayerGameSettingsException>(
                () => contendingService.ApplyToProfileAsync(
                    Path.Combine(temporary.Path, "."),
                    "activity"));

            var timeout = Assert.IsType<PathFileLockTimeoutException>(exception.InnerException);
            Assert.InRange(DateTime.UtcNow - waitStartedAt, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(layout.DataRoot, timeout.ResourcePath);
            Assert.Equal(Path.GetFullPath(lockPath), timeout.LockPath);
        }

        await contendingService.ApplyToProfileAsync(temporary.Path, "activity");

        var target = ReadOptions(Path.Combine(
            layout.GetProfileGameDirectory("activity"),
            "options.txt"));
        Assert.Equal("0.8", target["mouseSensitivity"]);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task AsyncLock_CanReleaseFromAnotherThreadAndBeReacquired()
    {
        using var temporary = new TemporaryDirectory();
        var resourcePath = temporary.Path;
        var lockPath = PlayerGameSettingsService.GetLockPath(resourcePath);
        var heldLock = await PathFileLock.AcquireAsync(
                resourcePath,
                lockPath,
                TimeSpan.FromSeconds(1),
                CancellationToken.None);
        var acquisitionThreadId = Environment.CurrentManagedThreadId;
        var released = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasingThread = new Thread(() =>
        {
            try
            {
                heldLock.Dispose();
                released.SetResult(Environment.CurrentManagedThreadId);
            }
            catch (Exception exception)
            {
                released.SetException(exception);
            }
        });

        releasingThread.Start();
        var releaseThreadId = await released.Task;

        Assert.NotEqual(acquisitionThreadId, releaseThreadId);
        using var recoveredLock = PathFileLock.Acquire(
            resourcePath,
            lockPath,
            TimeSpan.FromSeconds(1));
    }

    private static string CreateOptionsFile(
        ClientStorageLayout layout,
        string profileId,
        params string[] lines)
    {
        var path = Path.Combine(
            layout.GetProfileGameDirectory(profileId),
            "options.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static Dictionary<string, string> ReadOptions(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hechao-player-settings-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
