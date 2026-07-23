using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherApiClientAccountTests
{
    private static readonly HechaoAccount UnlinkedAccount = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "hechao",
        "赫朝",
        "owner@example.com",
        null,
        null,
        "default",
        AccessTier.Member,
        null,
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"));

    [Fact]
    public async Task RegisterAccountAsync_SendsAccountPayloadAndPersistsSession()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/auth/register", request.RequestUri!.AbsolutePath);
            var body = await request.Content!
                .ReadFromJsonAsync<HechaoAccountRegisterRequest>();
            Assert.Equal("hechao", body!.Username);
            Assert.Equal("赫朝", body.DisplayName);
            Assert.Equal("owner@example.com", body.Email);
            Assert.Equal("securepass123", body.Password);
            return JsonResponse(Session(UnlinkedAccount));
        });
        var store = new InMemorySessionStore(null);
        var client = CreateClient(handler, store);

        var account = await client.RegisterAccountAsync(
            "hechao",
            "赫朝",
            "securepass123",
            "owner@example.com");

        Assert.Equal(UnlinkedAccount, account);
        Assert.Equal("refresh-token", store.Session!.RefreshToken);
        Assert.Equal(UnlinkedAccount, store.Session.Account);
    }

    [Fact]
    public async Task RegisterAccountAsync_UsesFirstValidationMessage()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    title = "One or more validation errors occurred.",
                    errors = new Dictionary<string, string[]>
                    {
                        ["username"] = ["该赫朝账号名已被使用。"]
                    }
                })
            }));
        var client = CreateClient(handler, new InMemorySessionStore(null));

        var exception = await Assert.ThrowsAsync<LauncherApiException>(() =>
            client.RegisterAccountAsync(
                "hechao",
                "赫朝",
                "securepass123",
                null));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("该赫朝账号名已被使用。", exception.ApiDetail);
    }

    [Fact]
    public async Task LinkMinecraftIdentityAsync_UsesBearerAndUpdatesStoredAccount()
    {
        var linkedAccount = UnlinkedAccount with
        {
            MinecraftUuid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            MinecraftName = "HechaoPlayer",
            LuckPermsPrimaryGroup = "vip",
            AccessTier = AccessTier.Participant
        };
        var handler = new RecordingHandler(
            request =>
            {
                Assert.Equal("/v1/auth/refresh", request.RequestUri!.AbsolutePath);
                return Task.FromResult(JsonResponse(Session(UnlinkedAccount)));
            },
            async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/auth/minecraft/link", request.RequestUri!.AbsolutePath);
                Assert.Equal(
                    new AuthenticationHeaderValue("Bearer", "access-token"),
                    request.Headers.Authorization);
                var body = await request.Content!
                    .ReadFromJsonAsync<MinecraftIdentityLinkRequest>();
                Assert.Equal("minecraft-access-token", body!.MinecraftAccessToken);
                return JsonResponse(linkedAccount);
            });
        var store = new InMemorySessionStore(
            new StoredLauncherSession("refresh-token", UnlinkedAccount));
        var client = CreateClient(handler, store);

        await client.TryRestoreSessionAsync();
        var result = await client.LinkMinecraftIdentityAsync(
            "minecraft-access-token");

        Assert.Equal(linkedAccount, result);
        Assert.Equal(linkedAccount, store.Session!.Account);
        Assert.Equal(2, handler.RequestCount);
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

    private static AuthSessionResponse Session(HechaoAccount account)
    {
        return new AuthSessionResponse(
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "refresh-token",
            DateTimeOffset.UtcNow.AddDays(30),
            account);
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
