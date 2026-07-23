using System.IO;
using System.Text.Json;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Services;

public sealed record DownloadHistoryRecord(
    Guid Id,
    string ProfileId,
    string DisplayName,
    string Version,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DownloadJobStatus Status,
    long CompletedBytes,
    long TotalBytes,
    string CurrentFile,
    string? FailureMessage);

public interface IDownloadHistoryStore
{
    IReadOnlyList<DownloadHistoryRecord> Load();
    void Save(IEnumerable<DownloadHistoryRecord> records);
}

public sealed class JsonDownloadHistoryStore : IDownloadHistoryStore
{
    private const int MaximumHistoryItems = 100;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyPath;

    public JsonDownloadHistoryStore()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _historyPath = Path.Combine(
            localApplicationData,
            "Hechao",
            "Launcher",
            "download-history.json");
    }

    public IReadOnlyList<DownloadHistoryRecord> Load()
    {
        try
        {
            return File.Exists(_historyPath)
                ? JsonSerializer.Deserialize<List<DownloadHistoryRecord>>(
                      File.ReadAllText(_historyPath),
                      SerializerOptions) ?? []
                : [];
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<DownloadHistoryRecord> records)
    {
        var snapshot = records
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .Take(MaximumHistoryItems)
            .ToArray();
        var directory = Path.GetDirectoryName(_historyPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _historyPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(snapshot, SerializerOptions));
        File.Move(temporaryPath, _historyPath, overwrite: true);
    }
}
