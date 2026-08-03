namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerDirectoryAccessGateTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-directory-gate-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnterAsync_SerializesReadHandleAndParentDirectoryRename()
    {
        var source = Path.Combine(root, "ActivityNeoForge");
        var destination = Path.Combine(root, ".ActivityNeoForge.hechao-rollback");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "server.properties");
        await File.WriteAllTextAsync(path, "server-port=25568\n");
        var gate = new ServerDirectoryAccessGate();
        var readLease = await gate.EnterAsync(CancellationToken.None);
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var waiting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var move = Task.Run(async () =>
        {
            waiting.SetResult();
            using var moveLease = await gate.EnterAsync(CancellationToken.None);
            Directory.Move(source, destination);
        });

        await waiting.Task;
        Assert.False(move.IsCompleted);
        stream.Dispose();
        readLease.Dispose();

        await move.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(Path.Combine(destination, "server.properties")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
