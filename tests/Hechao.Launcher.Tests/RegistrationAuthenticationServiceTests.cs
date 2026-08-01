using System.Net;
using System.Text;
using System.Text.Json;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class RegistrationAuthenticationServiceTests
{
    [Fact]
    public async Task RegisterAsync_WhenAccountCreationSucceedsButLoginIsMalformed_ReportsPartialSuccess()
    {
        var forumRequestCount = 0;
        var forumClient = new ForumRegistrationClient(new HttpClient(
            new DelegateHandler(request =>
            {
                forumRequestCount++;
                Assert.Equal("/api/forum/register", request.RequestUri?.AbsolutePath);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }))
        {
            BaseAddress = new Uri("https://forum.example/")
        });
        var launcherApi = new LauncherApiClient(
            new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{",
                        Encoding.UTF8,
                        "application/json")
                }))
            {
                BaseAddress = new Uri("https://launcher-api.example/")
            },
            new InMemorySessionStore());
        var minecraftClient = new XboxMinecraftAuthenticationClient(
            new HttpClient(new DelegateHandler(_ =>
                throw new InvalidOperationException("Minecraft authentication is not expected."))));
        var service = new MicrosoftMinecraftAuthenticationService(
            launcherApi,
            forumClient,
            minecraftClient,
            microsoftClientId: null);

        var exception = await Assert.ThrowsAsync<RegistrationLoginFailedException>(() =>
            service.RegisterAsync(
                "tester",
                "测试玩家",
                "password",
                "tester@example.com",
                "123456"));

        Assert.Equal(1, forumRequestCount);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class InMemorySessionStore : ISecureSessionStore
    {
        public Task<StoredLauncherSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<StoredLauncherSession?>(null);
        }

        public Task SaveAsync(
            StoredLauncherSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
