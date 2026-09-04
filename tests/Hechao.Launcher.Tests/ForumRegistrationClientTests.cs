using System.Net;
using System.Net.Http.Json;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class ForumRegistrationClientTests
{
    [Fact]
    public async Task SendRegistrationCodeAsync_SendsEmailToForum()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/forum/send-code", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<EmailRequest>();
            Assert.Equal("player@example.com", body!.Email);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        await client.SendRegistrationCodeAsync("player@example.com");

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RegisterAsync_SendsUnifiedAccountPayload()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/forum/register", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<RegisterRequest>();
            Assert.Equal("player_one", body!.Username);
            Assert.Equal("玩家一号", body.DisplayName);
            Assert.Equal("player@example.com", body.Email);
            Assert.Equal("securepass123", body.Password);
            Assert.Equal("123456", body.Code);
            Assert.True(body.LegalAccepted);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        await client.RegisterAsync(
            "player_one",
            "玩家一号",
            "player@example.com",
            "securepass123",
            "123456",
            true);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RegisterAsync_ForwardsForumErrorMessage()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new
                {
                    error = "该邮箱已注册，请直接登录"
                })
            }));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ForumRegistrationException>(() =>
            client.RegisterAsync(
                "player_one",
                "玩家一号",
                "player@example.com",
                "securepass123",
                "123456",
                true));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("该邮箱已注册，请直接登录", exception.Detail);
    }

    private static ForumRegistrationClient CreateClient(HttpMessageHandler handler)
    {
        return new ForumRegistrationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hechao.world/"),
            Timeout = TimeSpan.FromSeconds(5)
        });
    }

    private sealed record EmailRequest(string Email);

    private sealed record RegisterRequest(
        string Username,
        string DisplayName,
        string Email,
        string Password,
        string Code,
        bool LegalAccepted);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return await response(request);
        }
    }
}
