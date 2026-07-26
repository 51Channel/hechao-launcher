using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.Launcher.Services;

public interface ILauncherTelemetryApiClient
{
    Task<LauncherTelemetryBatchResponse> SubmitTelemetryAsync(
        LauncherTelemetryBatchRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILauncherTelemetryService
{
    Task RecordAsync(
        LauncherTelemetryEventType type,
        LauncherTelemetryOutcome outcome,
        LauncherTelemetryFailureCode failureCode =
            LauncherTelemetryFailureCode.None,
        string? profileId = null,
        string? profileVersion = null,
        TimeSpan? duration = null,
        long? bytes = null);

    void TryFlush();
}

public sealed class NullLauncherTelemetryService : ILauncherTelemetryService
{
    public static NullLauncherTelemetryService Instance { get; } = new();

    private NullLauncherTelemetryService()
    {
    }

    public Task RecordAsync(
        LauncherTelemetryEventType type,
        LauncherTelemetryOutcome outcome,
        LauncherTelemetryFailureCode failureCode =
            LauncherTelemetryFailureCode.None,
        string? profileId = null,
        string? profileVersion = null,
        TimeSpan? duration = null,
        long? bytes = null) =>
        Task.CompletedTask;

    public void TryFlush()
    {
    }
}

public sealed class JsonLauncherTelemetryService : ILauncherTelemetryService
{
    private const int MaximumOutboxItems = 500;
    private const int MaximumBatchItems = 50;
    private static readonly TimeSpan MaximumEventAge = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();

    private readonly ILauncherTelemetryApiClient _apiClient;
    private readonly string _outboxPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _outboxGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly List<LauncherTelemetryEvent> _outbox;

    public JsonLauncherTelemetryService(ILauncherTelemetryApiClient apiClient)
        : this(apiClient, GetDefaultOutboxPath(), TimeProvider.System)
    {
    }

    internal JsonLauncherTelemetryService(
        ILauncherTelemetryApiClient apiClient,
        string outboxPath,
        TimeProvider timeProvider)
    {
        _apiClient = apiClient;
        _outboxPath = outboxPath;
        _timeProvider = timeProvider;
        _outbox = LoadOutbox();
    }

    internal int PendingCount
    {
        get
        {
            _outboxGate.Wait();
            try
            {
                return _outbox.Count;
            }
            finally
            {
                _outboxGate.Release();
            }
        }
    }

    public async Task RecordAsync(
        LauncherTelemetryEventType type,
        LauncherTelemetryOutcome outcome,
        LauncherTelemetryFailureCode failureCode =
            LauncherTelemetryFailureCode.None,
        string? profileId = null,
        string? profileVersion = null,
        TimeSpan? duration = null,
        long? bytes = null)
    {
        try
        {
            var hasProfile = !string.IsNullOrWhiteSpace(profileId) &&
                             !string.IsNullOrWhiteSpace(profileVersion);
            failureCode = outcome == LauncherTelemetryOutcome.Success
                ? LauncherTelemetryFailureCode.None
                : failureCode == LauncherTelemetryFailureCode.None
                    ? LauncherTelemetryFailureCode.Unexpected
                    : failureCode;
            var durationMilliseconds = duration is null
                ? null
                : (int?)Math.Clamp(
                    (long)Math.Round(duration.Value.TotalMilliseconds),
                    0,
                    86_400_000);
            var item = new LauncherTelemetryEvent(
                Guid.NewGuid(),
                type,
                outcome,
                failureCode,
                LauncherProductInfo.Version,
                _timeProvider.GetUtcNow(),
                hasProfile ? profileId!.Trim() : null,
                hasProfile ? profileVersion!.Trim() : null,
                durationMilliseconds,
                bytes is null ? null : Math.Clamp(bytes.Value, 0, 1_099_511_627_776));

            await _outboxGate.WaitAsync();
            try
            {
                _outbox.Add(item);
                TrimOutbox();
                await SaveOutboxAsync();
            }
            finally
            {
                _outboxGate.Release();
            }

            TryFlush();
        }
        catch (Exception)
        {
        }
    }

    public void TryFlush()
    {
        _ = FlushCoreAsync();
    }

    internal async Task FlushAsync()
    {
        await FlushCoreAsync();
    }

    private async Task FlushCoreAsync()
    {
        if (!await _flushGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (true)
            {
                LauncherTelemetryEvent[] batch;
                await _outboxGate.WaitAsync();
                try
                {
                    batch = _outbox.Take(MaximumBatchItems).ToArray();
                }
                finally
                {
                    _outboxGate.Release();
                }

                if (batch.Length == 0)
                {
                    return;
                }

                var response = await _apiClient.SubmitTelemetryAsync(
                    new LauncherTelemetryBatchRequest(batch));
                if (response.Accepted + response.Duplicates != batch.Length)
                {
                    return;
                }

                var submitted = batch.Select(item => item.EventId).ToHashSet();
                await _outboxGate.WaitAsync();
                try
                {
                    _outbox.RemoveAll(item => submitted.Contains(item.EventId));
                    await SaveOutboxAsync();
                }
                finally
                {
                    _outboxGate.Release();
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private List<LauncherTelemetryEvent> LoadOutbox()
    {
        try
        {
            if (!File.Exists(_outboxPath))
            {
                return [];
            }

            var cutoff = _timeProvider.GetUtcNow() - MaximumEventAge;
            return (JsonSerializer.Deserialize<List<LauncherTelemetryEvent>>(
                        File.ReadAllText(_outboxPath),
                        SerializerOptions) ?? [])
                .Where(item => item.EventId != Guid.Empty && item.OccurredAt >= cutoff)
                .DistinctBy(item => item.EventId)
                .TakeLast(MaximumOutboxItems)
                .ToList();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException)
        {
            return [];
        }
    }

    private void TrimOutbox()
    {
        var cutoff = _timeProvider.GetUtcNow() - MaximumEventAge;
        _outbox.RemoveAll(item => item.OccurredAt < cutoff);
        if (_outbox.Count > MaximumOutboxItems)
        {
            _outbox.RemoveRange(0, _outbox.Count - MaximumOutboxItems);
        }
    }

    private async Task SaveOutboxAsync()
    {
        var directory = Path.GetDirectoryName(_outboxPath)
            ?? throw new InvalidOperationException(
                "The launcher telemetry path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _outboxPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(_outbox, SerializerOptions));
        File.Move(temporaryPath, _outboxPath, overwrite: true);
    }

    private static string GetDefaultOutboxPath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localApplicationData,
            "Hechao",
            "Launcher",
            "telemetry-outbox.json");
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
