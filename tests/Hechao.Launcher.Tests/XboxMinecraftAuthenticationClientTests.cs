using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class XboxMinecraftAuthenticationClientTests
{
    private static readonly JsonSerializerOptions ExactPropertyNames = new()
    {
        PropertyNamingPolicy = null
    };

    [Fact]
    public async Task AuthenticateAsync_UsesXboxContractCasingAndHeader()
    {
        var handler = new RecordingHandler(
            async request =>
            {
                Assert.Equal(
                    "https://user.auth.xboxlive.com/user/authenticate",
                    request.RequestUri!.AbsoluteUri);
                AssertXboxContractHeader(request);
                using var document = await ReadBodyAsync(request);
                Assert.True(document.RootElement.TryGetProperty("Properties", out var properties));
                Assert.False(document.RootElement.TryGetProperty("properties", out _));
                Assert.Equal(
                    "d=microsoft-access-token",
                    properties.GetProperty("RpsTicket").GetString());
                Assert.Equal(
                    "http://auth.xboxlive.com",
                    document.RootElement.GetProperty("RelyingParty").GetString());
                return JsonResponse(XboxToken("xbox-user-token"));
            },
            async request =>
            {
                Assert.Equal(
                    "https://xsts.auth.xboxlive.com/xsts/authorize",
                    request.RequestUri!.AbsoluteUri);
                AssertXboxContractHeader(request);
                using var document = await ReadBodyAsync(request);
                Assert.Equal(
                    "RETAIL",
                    document.RootElement
                        .GetProperty("Properties")
                        .GetProperty("SandboxId")
                        .GetString());
                Assert.Equal(
                    "xbox-user-token",
                    document.RootElement
                        .GetProperty("Properties")
                        .GetProperty("UserTokens")[0]
                        .GetString());
                return JsonResponse(XboxToken("xsts-token"));
            },
            async request =>
            {
                Assert.Equal(
                    "https://api.minecraftservices.com/authentication/login_with_xbox",
                    request.RequestUri!.AbsoluteUri);
                Assert.False(request.Headers.Contains("x-xbl-contract-version"));
                using var document = await ReadBodyAsync(request);
                Assert.Equal(
                    "XBL3.0 x=user-hash;xsts-token",
                    document.RootElement.GetProperty("identityToken").GetString());
                return JsonResponse(new
                {
                    access_token = "minecraft-access-token",
                    expires_in = 3600
                });
            });
        var client = new XboxMinecraftAuthenticationClient(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

        var result = await client.AuthenticateAsync("microsoft-access-token");

        Assert.Equal("minecraft-access-token", result.AccessToken);
        Assert.Equal("1234567890", result.Xuid);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(55));
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task AuthenticateAsync_MapsMinecraftApplicationRejection()
    {
        var handler = new RecordingHandler(
            _ => Task.FromResult(JsonResponse(XboxToken("xbox-user-token"))),
            _ => Task.FromResult(JsonResponse(XboxToken("xsts-token"))),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new
                {
                    error = "ForbiddenOperationException",
                    errorMessage =
                        "Invalid app registration, see https://aka.ms/AppRegInfo for more information"
                })
            }));
        var client = new XboxMinecraftAuthenticationClient(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

        var exception = await Assert.ThrowsAsync<MinecraftSignInException>(
            () => client.AuthenticateAsync("microsoft-access-token"));

        Assert.Equal(MinecraftSignInFailure.ApplicationNotApproved, exception.Failure);
        Assert.Equal(3, handler.RequestCount);
    }

    private static object XboxToken(string token)
    {
        return new
        {
            Token = token,
            DisplayClaims = new
            {
                xui = new[]
                {
                    new
                    {
                        uhs = "user-hash",
                        xid = "1234567890"
                    }
                }
            }
        };
    }

    private static void AssertXboxContractHeader(HttpRequestMessage request)
    {
        Assert.True(request.Headers.TryGetValues(
            "x-xbl-contract-version",
            out var values));
        Assert.Equal(["1"], values);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request)
    {
        Assert.NotNull(request.Content);
        await using var stream = await request.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value, options: ExactPropertyNames)
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
}
