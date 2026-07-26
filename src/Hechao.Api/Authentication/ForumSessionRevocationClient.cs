using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Authentication;

public sealed class ForumSessionRevocationClient
{
    private const string EndpointPath = "api/internal/hechao/session-revoke";
    private readonly HttpClient _httpClient;
    private readonly ForumSessionRevocationOptions _options;
    private readonly Uri _endpoint;

    public ForumSessionRevocationClient(
        HttpClient httpClient,
        IOptions<ForumSessionRevocationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        if (!_options.TryGetBaseUri(out var baseUri))
        {
            throw new InvalidOperationException(
                "Forum session revocation base URL is invalid.");
        }

        _endpoint = new Uri(baseUri, EndpointPath);
    }

    public async Task DeliverAsync(
        ForumSessionRevocationDelivery delivery,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.TryAddWithoutValidation(
            "X-Hechao-Session-Token",
            _options.InternalToken);
        request.Content = JsonContent.Create(new
        {
            requestId = delivery.RequestId,
            userId = delivery.UserId
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Forum session revocation returned HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }
    }
}
