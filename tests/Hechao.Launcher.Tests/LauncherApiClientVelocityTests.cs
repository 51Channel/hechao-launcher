using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherApiClientVelocityTests
{
    private static readonly AuthenticatedPlayer Player = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "HechaoPlayer",
        "vip",
        AccessTier.Participant,
        DateTimeOffset.Parse("2026-07-23T10:00:00Z"));

    [Fact]
    public async Task CreateVelocityLaunchGrantAsync_SendsAuthenticatedServerRequest()
    {
        var expiresAt = DateTimeOffset.Parse("2026-07-23T10:10:00Z");
        var handler = new RecordingHandler(
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/auth/refresh", request.RequestUri!.AbsolutePath);
                return Task.FromResult(JsonResponse(Session("access-one", "refresh-two")));
            },
            async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/velocity/launch-grants", request.RequestUri!.AbsolutePath);
                Assert.Equal(
                    new AuthenticationHeaderValue("Bearer", "access-one"),
                    request.Headers.Authorization);

                var body = await request.Content!.ReadFromJsonAsync<VelocityLaunchGrantRequest>();
                Assert.Equal("activity", body!.ServerId);
                return JsonResponse(new VelocityLaunchGrantResponse(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "activity",
                    expiresAt));
            });
        var store = new InMemorySessionStore(new StoredLauncherSession("refresh-one", Player));
        var client = CreateClient(handler, store);

        var restored = await client.TryRestoreSessionAsync();
        var grant = await client.CreateVelocityLaunchGrantAsync("activity");

        Assert.Equal(Player, restored);
        Assert.Equal("activity", grant.ServerId);
        Assert.Equal(expiresAt, grant.ExpiresAt);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("refresh-two", store.Session!.RefreshToken);
    }

    [Fact]
    public async Task CreateVelocityLaunchGrantAsync_WithoutSession_DoesNotCallApi()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("The API must not be called without a session.")));
        var client = CreateClient(handler, new InMemorySessionStore(session: null));

        await Assert.ThrowsAsync<LauncherAuthenticationRequiredException>(
            () => client.CreateVelocityLaunchGrantAsync("activity"));

        Assert.Equal(0, handler.RequestCount);
    }

    private static LauncherApiClient CreateClient(
        HttpMessageHandler handler,
        ISecureSessionStore store)
    {
        return new LauncherApiClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://launcher-api.example/"),
                Timeout = TimeSpan.FromSeconds(5)
            },
            store);
    }

    private static AuthSessionResponse Session(string accessToken, string refreshToken)
    {
        return new AuthSessionResponse(
            accessToken,
            DateTimeOffset.UtcNow.AddMinutes(15),
            refreshToken,
            DateTimeOffset.UtcNow.AddDays(30),
            Player);
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }

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
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Assert.NotEmpty(_responses);
            return await _responses.Dequeue()(request);
        }
    }

    private sealed class InMemorySessionStore(StoredLauncherSession? session)
        : ISecureSessionStore
    {
        public StoredLauncherSession? Session { get; private set; } = session;

        public Task<StoredLauncherSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Session);
        }

        public Task SaveAsync(
            StoredLauncherSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Session = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Session = null;
            return Task.CompletedTask;
        }
    }
}
