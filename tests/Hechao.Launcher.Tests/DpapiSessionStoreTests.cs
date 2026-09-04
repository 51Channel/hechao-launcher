using System.ComponentModel;
using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class DpapiSessionStoreTests
{
    private static readonly HechaoAccount Account = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "session-test",
        "会话测试",
        null,
        null,
        null,
        "default",
        AccessTier.Member,
        null,
        DateTimeOffset.Parse("2026-08-01T00:00:00Z"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsTheCompleteSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "session.dat");
        var expected = new StoredLauncherSession(
            "refresh-token",
            Account,
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(10),
            DateTimeOffset.UtcNow.AddDays(30));
        var store = new DpapiSessionStore(path);

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Load_WhenDpapiRestoreFailsPreservesTheSessionFile()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "session.dat");
        var store = new DpapiSessionStore(
            path,
            protect: bytes => bytes,
            unprotect: _ => throw new Win32Exception(13, "synthetic DPAPI failure"));

        await store.SaveAsync(new StoredLauncherSession("refresh-token", Account));
        var actual = await store.LoadAsync();

        Assert.Null(actual);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Load_WhenCurrentFileIsCorruptFallsBackToEncryptedBackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "session.dat");
        var store = new DpapiSessionStore(path);
        var first = new StoredLauncherSession("refresh-one", Account);
        var second = new StoredLauncherSession("refresh-two", Account);

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await File.WriteAllBytesAsync(path, [0x01, 0x02, 0x03]);

        var actual = await store.LoadAsync();

        Assert.Equal(first, actual);
    }

    [Fact]
    public async Task Save_FromMultipleStoreInstancesLeavesOneValidAtomicSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "session.dat");
        var sessions = Enumerable.Range(0, 8)
            .Select(index => new StoredLauncherSession(
                $"refresh-{index}",
                Account,
                $"access-{index}",
                DateTimeOffset.UtcNow.AddMinutes(10),
                DateTimeOffset.UtcNow.AddDays(30)))
            .ToArray();

        await Task.WhenAll(sessions.Select(session =>
            new DpapiSessionStore(path).SaveAsync(session)));

        var actual = await new DpapiSessionStore(path).LoadAsync();

        Assert.NotNull(actual);
        Assert.Contains(actual, sessions);
        Assert.Empty(Directory.GetFiles(temporary.Path, ".session.dat.*.tmp"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Launcher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
