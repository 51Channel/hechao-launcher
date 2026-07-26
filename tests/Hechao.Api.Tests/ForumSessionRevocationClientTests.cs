using System.Net;
using System.Text.Json;
using Hechao.Api.Authentication;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class ForumSessionRevocationClientTests
{
    [Fact]
    public async Task DeliverAsync_SendsAuthenticatedStableIdentifiers()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        await client.DeliverAsync(
            new ForumSessionRevocationDelivery(requestId, userId, 1),
            CancellationToken.None);

        Assert.Equal(
            "http://127.0.0.1:3000/api/internal/hechao/session-revoke",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("test-session-revocation-token-000001", handler.Token);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            requestId,
            body.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(userId, body.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task DeliverAsync_RejectsNonSuccessWithoutReadingResponseBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DeliverAsync(
                new ForumSessionRevocationDelivery(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.DoesNotContain("secret response", exception.Message);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(7, 300)]
    [InlineData(20, 300)]
    public void RetryDelay_IsBoundedExponential(int attempt, int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            ForumSessionRevocationDeliveryService.CalculateRetryDelay(attempt));
    }

    private static ForumSessionRevocationClient CreateClient(
        HttpMessageHandler handler)
    {
        return new ForumSessionRevocationClient(
            new HttpClient(handler),
            Options.Create(new ForumSessionRevocationOptions
            {
                Enabled = true,
                BaseUrl = "http://127.0.0.1:3000/",
                InternalToken = "test-session-revocation-token-000001",
                RequestTimeoutSeconds = 5
            }));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Token { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Token = request.Headers.GetValues("X-Hechao-Session-Token").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("secret response")
            };
        }
    }
}
