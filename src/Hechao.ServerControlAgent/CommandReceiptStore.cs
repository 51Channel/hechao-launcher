using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed record AgentCommandResult(
    ServerControlCommandOutcome Outcome,
    string ResultCode,
    string ResultMessage);

internal sealed record StoredCommandReceipt(
    Guid CommandId,
    DateTimeOffset CompletedAt,
    AgentCommandResult Result);

internal sealed class CommandReceiptStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    internal CommandReceiptStore(string stateDirectory)
    {
        _directory = Path.Combine(
            Path.GetFullPath(stateDirectory),
            "command-receipts");
        Directory.CreateDirectory(_directory);
    }

    internal StoredCommandReceipt? TryRead(Guid commandId)
    {
        var path = GetPath(commandId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredCommandReceipt>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException)
        {
            return null;
        }
    }

    internal void Save(Guid commandId, AgentCommandResult result)
    {
        var receipt = new StoredCommandReceipt(
            commandId,
            DateTimeOffset.UtcNow,
            result);
        var path = GetPath(commandId);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(receipt, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        foreach (var file in files.Skip(5000).Concat(
                     files.Where(file => file.LastWriteTimeUtc < cutoff.UtcDateTime)))
        {
            try
            {
                file.Delete();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private string GetPath(Guid commandId) =>
        Path.Combine(_directory, $"{commandId:N}.json");
}
