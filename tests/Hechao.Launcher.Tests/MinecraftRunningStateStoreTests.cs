using Hechao.Launcher.Services;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftRunningStateStoreTests
{
    [Fact]
    public void SaveLoadAndClear_RoundTripsOnlyMatchingProcess()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-running-state-tests",
            Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(root, "running-game.json");
        try
        {
            var store = new JsonMinecraftRunningStateStore(statePath);
            var startedAt = DateTimeOffset.UtcNow;
        var expected = new PersistedMinecraftProcess(
            "base-1.21.11",
            "survival2",
            1234,
            Path.Combine(root, "runtime", "bin", "javaw.exe"),
            startedAt,
            Path.Combine(root, "game-data"));

            store.Save(expected);

            Assert.Equal(expected, store.Load());
            store.ClearIfMatches(4321, startedAt);
            Assert.NotNull(store.Load());
            store.ClearIfMatches(1234, startedAt.AddSeconds(3));
            Assert.NotNull(store.Load());
            store.ClearIfMatches(1234, startedAt.AddSeconds(1));
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_OldStateWithoutDataRoot_RemainsCompatible()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-running-state-tests",
            Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(root, "running-game.json");
        Directory.CreateDirectory(root);
        var startedAt = DateTimeOffset.UtcNow;
        var executablePath = Path.Combine(root, "runtime", "bin", "javaw.exe");
        File.WriteAllText(
            statePath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                ProfileId = "base-1.21.11",
                ServerId = "survival2",
                ProcessId = 1234,
                ExecutablePath = executablePath,
                StartedAt = startedAt
            }));

        var loaded = new JsonMinecraftRunningStateStore(statePath).Load();

        Assert.NotNull(loaded);
        Assert.Equal(Path.GetFullPath(executablePath), loaded.ExecutablePath);
        Assert.Null(loaded.DataRoot);
    }

    [Fact]
    public void Load_WhenStateIsMalformed_ReturnsNull()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-running-state-tests",
            Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(root, "running-game.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(statePath, "{not-json");

            var store = new JsonMinecraftRunningStateStore(statePath);

        Assert.Null(store.Load());
    }

        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SameNormalizedStatePath_SerializesConcurrentSavesWithoutPartialState()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "running-game.json");
        var firstStore = new JsonMinecraftRunningStateStore(statePath);
        var secondStore = new JsonMinecraftRunningStateStore(
            Path.Combine(temporary.Path, ".", "running-game.json"));
        var firstProcess = CreateProcess(1201);
        var secondProcess = CreateProcess(1202);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var writes = Enumerable.Range(0, 40)
            .Select(index => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                (index % 2 == 0 ? firstStore : secondStore)
                    .Save(index % 2 == 0 ? firstProcess : secondProcess);
            }))
            .ToArray();

        start.SetResult(true);
        await Task.WhenAll(writes);

        var loaded = firstStore.Load();
        Assert.NotNull(loaded);
        Assert.Contains(loaded.ProcessId, new[] { firstProcess.ProcessId, secondProcess.ProcessId });
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void HeldLock_TimesOutAndRecoversAfterOwnerHandleCloses()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "running-game.json");
        var ownerStore = new JsonMinecraftRunningStateStore(statePath);
        var contendingStore = new JsonMinecraftRunningStateStore(
            Path.Combine(temporary.Path, ".", "running-game.json"),
            TimeSpan.FromMilliseconds(100));

        using (PathFileLock.Acquire(
                   statePath,
                   ownerStore.LockPath,
                   TimeSpan.FromSeconds(1)))
        {
            var waitStartedAt = DateTime.UtcNow;
            var exception = Assert.Throws<PathFileLockTimeoutException>(
                () => contendingStore.Save(CreateProcess(1301)));

            Assert.InRange(DateTime.UtcNow - waitStartedAt, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(Path.GetFullPath(statePath), exception.ResourcePath);
            Assert.Equal(Path.GetFullPath(ownerStore.LockPath), exception.LockPath);
        }

        contendingStore.Save(CreateProcess(1302));

        Assert.True(File.Exists(ownerStore.LockPath));
        Assert.Equal(1302, ownerStore.Load()?.ProcessId);
    }

    private static PersistedMinecraftProcess CreateProcess(int processId)
    {
        return new PersistedMinecraftProcess(
            "base-1.21.11",
            "survival2",
            processId,
            Path.Combine(Path.GetTempPath(), "javaw.exe"),
            DateTimeOffset.UtcNow,
            Path.Combine(Path.GetTempPath(), "hechao-game-data"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hechao-running-state-tests-{Guid.NewGuid():N}");
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
