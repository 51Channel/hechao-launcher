using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherTelemetryServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsWhileOfflineAndFlushesIdempotentBatch()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "telemetry-outbox.json");
        var api = new RecordingTelemetryApiClient
        {
            Failure = new HttpRequestException("offline")
        };
        var service = new JsonLauncherTelemetryService(
            api,
            path,
            TimeProvider.System);

        await service.RecordAsync(
            LauncherTelemetryEventType.Install,
            LauncherTelemetryOutcome.Failure,
            LauncherTelemetryFailureCode.NetworkUnavailable,
            "base-1.21.11",
            "1.0.5",
            TimeSpan.FromSeconds(5),
            4096);
        await api.FirstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, service.PendingCount);
        Assert.True(File.Exists(path));
        Assert.DoesNotContain(
            Environment.UserName,
            await File.ReadAllTextAsync(path),
            StringComparison.OrdinalIgnoreCase);

        api.Failure = null;
        await service.FlushAsync();

        Assert.Equal(0, service.PendingCount);
        var submitted = Assert.Single(api.SuccessfulBatches);
        var item = Assert.Single(submitted.Events);
        Assert.Equal(LauncherTelemetryFailureCode.NetworkUnavailable, item.FailureCode);
        Assert.Equal("base-1.21.11", item.ProfileId);
        Assert.Equal(4096, item.Bytes);
    }

    [Fact]
    public async Task Constructor_DiscardsExpiredAndDuplicateOutboxEntries()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "telemetry-outbox.json");
        var now = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();
        var current = Event(eventId, now.AddMinutes(-1));
        var expired = Event(Guid.NewGuid(), now.AddDays(-31));
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                new[] { current, current, expired },
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters =
                    {
                        new System.Text.Json.Serialization.JsonStringEnumConverter()
                    }
                }));

        var service = new JsonLauncherTelemetryService(
            new RecordingTelemetryApiClient
            {
                Failure = new HttpRequestException("offline")
            },
            path,
            TimeProvider.System);

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public async Task FlushAsync_DrainsOutboxInBoundedBatches()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "telemetry-outbox.json");
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 51)
            .Select(_ => Event(Guid.NewGuid(), now))
            .ToArray();
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                items,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters =
                    {
                        new System.Text.Json.Serialization.JsonStringEnumConverter()
                    }
                }));
        var api = new RecordingTelemetryApiClient();
        var service = new JsonLauncherTelemetryService(
            api,
            path,
            TimeProvider.System);

        await service.FlushAsync();

        Assert.Equal(0, service.PendingCount);
        Assert.Equal([50, 1], api.SuccessfulBatches
            .Select(batch => batch.Events.Count)
            .ToArray());
    }

    private static LauncherTelemetryEvent Event(
        Guid eventId,
        DateTimeOffset occurredAt) =>
        new(
            eventId,
            LauncherTelemetryEventType.LauncherStarted,
            LauncherTelemetryOutcome.Success,
            LauncherTelemetryFailureCode.None,
            "0.11.13",
            occurredAt,
            null,
            null,
            null,
            null);

    private sealed class RecordingTelemetryApiClient
        : ILauncherTelemetryApiClient
    {
        public Exception? Failure { get; set; }
        public List<LauncherTelemetryBatchRequest> SuccessfulBatches { get; } = [];
        public TaskCompletionSource FirstAttempt { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LauncherTelemetryBatchResponse> SubmitTelemetryAsync(
            LauncherTelemetryBatchRequest request,
            CancellationToken cancellationToken = default)
        {
            FirstAttempt.TrySetResult();
            if (Failure is not null)
            {
                return Task.FromException<LauncherTelemetryBatchResponse>(Failure);
            }

            SuccessfulBatches.Add(request);
            return Task.FromResult(new LauncherTelemetryBatchResponse(
                request.Events.Count,
                0));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hechao-telemetry-tests",
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
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
