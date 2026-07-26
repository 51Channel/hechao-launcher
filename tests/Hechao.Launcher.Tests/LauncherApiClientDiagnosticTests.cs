using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherApiClientDiagnosticTests
{
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
    public async Task DiagnosticUpload_UsesBearerThenOneTimeHeaderAndStreamsArchive()
    {
        var uploadId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var token = new string('A', 43);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var archiveBytes = Encoding.UTF8.GetBytes("PK\u0003\u0004diagnostic");
        var handler = new RecordingHandler(
            request =>
            {
                Assert.Equal("/v1/auth/refresh", request.RequestUri!.AbsolutePath);
                return Task.FromResult(JsonResponse(Session()));
            },
            async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/diagnostics/uploads", request.RequestUri!.AbsolutePath);
                Assert.Equal(
                    new AuthenticationHeaderValue("Bearer", "access-token"),
                    request.Headers.Authorization);
                var body = await request.Content!
                    .ReadFromJsonAsync<DiagnosticUploadCreateRequest>();
                Assert.Equal("base-1.21.11", body!.ProfileId);
                Assert.Equal(archiveBytes.Length, body.Size);
                return JsonResponse(
                    new DiagnosticUploadAuthorizationResponse(
                        uploadId,
                        token,
                        expiresAt,
                        8 * 1024 * 1024),
                    HttpStatusCode.Created);
            },
            async request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal(
                    $"/v1/diagnostics/uploads/{uploadId:D}",
                    request.RequestUri!.AbsolutePath);
                Assert.Equal(
                    token,
                    request.Headers.GetValues("X-Hechao-Diagnostic-Token").Single());
                Assert.Equal("application/zip", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(archiveBytes.Length, request.Content.Headers.ContentLength);
                Assert.Equal(
                    archiveBytes,
                    await request.Content.ReadAsByteArrayAsync());
                return JsonResponse(new DiagnosticUploadReceipt(
                    uploadId,
                    "base-1.21.11",
                    archiveBytes.Length,
                    new string('a', 64),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(14)));
            });
        var store = new InMemorySessionStore(
            new StoredLauncherSession("refresh-token", Account));
        var client = CreateClient(handler, store);
        await client.TryRestoreSessionAsync();

        var authorization = await client.CreateDiagnosticUploadAsync(
            new DiagnosticUploadCreateRequest(
                "base-1.21.11",
                archiveBytes.Length,
                new string('a', 64),
                "0.11.12"));
        await using var stream = new MemoryStream(archiveBytes);
        var receipt = await client.UploadDiagnosticAsync(
            authorization,
            stream,
            archiveBytes.Length);

        Assert.Equal(uploadId, receipt.UploadId);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task CreateDiagnosticUploadAsync_WithoutSession_DoesNotCallApi()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("Unexpected API request.")));
        var client = CreateClient(
            handler,
            new InMemorySessionStore(session: null));

        await Assert.ThrowsAsync<LauncherAuthenticationRequiredException>(() =>
            client.CreateDiagnosticUploadAsync(new DiagnosticUploadCreateRequest(
                "base-1.21.11",
                1024,
                new string('a', 64),
                "0.11.12")));

        Assert.Equal(0, handler.RequestCount);
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
