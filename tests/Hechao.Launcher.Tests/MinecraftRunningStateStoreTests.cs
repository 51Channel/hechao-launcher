using Hechao.Launcher.Services;

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
}
