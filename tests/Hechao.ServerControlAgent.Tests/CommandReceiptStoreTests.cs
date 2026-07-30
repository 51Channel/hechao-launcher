using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class CommandReceiptStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "hechao-receipts", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_PersistsResultForIdempotentRedelivery()
    {
        var store = new CommandReceiptStore(_root);
        var commandId = Guid.NewGuid();
        var result = new AgentCommandResult(
            ServerControlCommandOutcome.Succeeded,
            "COMMAND_SENT",
            "done");

        store.Save(commandId, result);
        var receipt = store.TryRead(commandId);

        Assert.NotNull(receipt);
        Assert.Equal(commandId, receipt.CommandId);
        Assert.Equal(result, receipt.Result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
