using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherApiClientTelemetryTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    private static readonly HechaoAccount Account = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "player",
        "玩家",
        null,
        null,
        null,
        "default",
        AccessTier.Member,
        null,
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"));

    [Fact]
    public async Task SubmitTelemetryAsync_UsesAuthenticatedBatchEndpoint()
    {
        var handler = new RecordingHandler(
            request =>
            {
                Assert.Equal("/v1/auth/refresh", request.RequestUri!.AbsolutePath);
                return Task.FromResult(JsonResponse(Session()));
            },
            async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/telemetry/events", request.RequestUri!.AbsolutePath);
                Assert.Equal(
                    new AuthenticationHeaderValue("Bearer", "access-token"),
                    request.Headers.Authorization);
                var body = await request.Content!
                    .ReadFromJsonAsync<LauncherTelemetryBatchRequest>(
                        SerializerOptions);
                Assert.Single(body!.Events);
                return JsonResponse(new LauncherTelemetryBatchResponse(1, 0));
            });
        var store = new InMemorySessionStore(
            new StoredLauncherSession("refresh-token", Account));
        var client = CreateClient(handler, store);
        await client.TryRestoreSessionAsync();
        var request = new LauncherTelemetryBatchRequest(
        [
            new LauncherTelemetryEvent(
                Guid.NewGuid(),
                LauncherTelemetryEventType.LauncherStarted,
                LauncherTelemetryOutcome.Success,
                LauncherTelemetryFailureCode.None,
                "0.11.13",
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null)
        ]);

        var result = await client.SubmitTelemetryAsync(request);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(2, handler.RequestCount);
    }

    private static LauncherApiClient CreateClient(
        HttpMessageHandler handler,
        ISecureSessionStore store)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://launcher-api.example/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        return new LauncherApiClient(httpClient, httpClient, store);
    }

    private static AuthSessionResponse Session() =>
        new(
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "refresh-token-2",
            DateTimeOffset.UtcNow.AddDays(30),
            Account);

    private static HttpResponseMessage JsonResponse<T>(
        T value,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = JsonContent.Create(value)
        };

    private sealed class RecordingHandler(
        params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responses =
            new(responses);

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return await _responses.Dequeue()(request);
        }
    }

    private sealed class InMemorySessionStore(StoredLauncherSession? session)
        : ISecureSessionStore
    {
        public StoredLauncherSession? Session { get; private set; } = session;

        public Task<StoredLauncherSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task SaveAsync(
            StoredLauncherSession session,
            CancellationToken cancellationToken = default)
        {
            Session = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Session = null;
            return Task.CompletedTask;
        }
    }
}
