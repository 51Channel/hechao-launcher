namespace Hechao.ServerControlAgent.Tests;

public sealed class TransientFileSystemTests
{
    [Fact]
    public void ExecuteWithSharingRetry_RetriesSharingViolationAndSucceeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        TransientFileSystem.ExecuteWithSharingRetry(
            () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException(
                        "sharing violation",
                        unchecked((int)0x80070020));
                }
            },
            delays.Add);

        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)],
            delays);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020))]
    [InlineData(unchecked((int)0x80070021))]
    public void ExecuteWithSharingRetry_StopsAfterBoundedAttempts(int hresult)
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var exception = Assert.Throws<IOException>(() =>
            TransientFileSystem.ExecuteWithSharingRetry(
                () =>
                {
                    attempts++;
                    throw new IOException("locked", hresult);
                },
                delays.Add));

        Assert.Equal(hresult, exception.HResult);
        Assert.Equal(6, attempts);
        Assert.Equal(5, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1550), delays.Aggregate(
            TimeSpan.Zero,
            (total, delay) => total + delay));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070002))]
    [InlineData(unchecked((int)0x80070005))]
    public void ExecuteWithSharingRetry_DoesNotRetryOtherIoFailures(int hresult)
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        Assert.Throws<IOException>(() =>
            TransientFileSystem.ExecuteWithSharingRetry(
                () =>
                {
                    attempts++;
                    throw new IOException(
                        "file not found",
                        hresult);
                },
                delays.Add));

        Assert.Equal(1, attempts);
        Assert.Empty(delays);
    }

    [Fact]
    public void ExecuteDirectoryMoveWithRetry_RetriesWindowsDirectoryInUse()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "hechao-directory-retry-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "ActivityNeoForge");
        var destination = Path.Combine(root, ".ActivityNeoForge.hechao-rollback");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "server.properties");
        File.WriteAllText(path, "server-port=25568\n");
        using var stream = File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var delays = new List<TimeSpan>();

        try
        {
            TransientFileSystem.ExecuteDirectoryMoveWithRetry(
                () => Directory.Move(source, destination),
                delay =>
                {
                    delays.Add(delay);
                    stream.Dispose();
                });

            Assert.Single(delays);
            Assert.True(File.Exists(
                Path.Combine(destination, "server.properties")));
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
