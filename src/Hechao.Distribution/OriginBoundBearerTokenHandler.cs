using System.Net.Http.Headers;

namespace Hechao.Distribution;

public sealed class OriginBoundBearerTokenHandler(
    Uri authorizedOrigin,
    Func<CancellationToken, ValueTask<string>> accessTokenProvider) : DelegatingHandler
{
    private readonly Uri _authorizedOrigin = NormalizeOrigin(authorizedOrigin);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } requestUri &&
            IsSameOrigin(requestUri, _authorizedOrigin))
        {
            var accessToken = await accessTokenProvider(cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("The launcher access token is unavailable.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else
        {
            request.Headers.Authorization = null;
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsSameOrigin(Uri candidate, Uri origin) =>
        string.Equals(candidate.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == origin.Port;

    private static Uri NormalizeOrigin(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri ||
            (value.Scheme != Uri.UriSchemeHttps &&
             !(value.Scheme == Uri.UriSchemeHttp && value.IsLoopback)))
        {
            throw new ArgumentException(
                "The authorized origin must use HTTPS, except for loopback development URLs.",
                nameof(value));
        }

        return new UriBuilder(value.Scheme, value.IdnHost, value.Port).Uri;
    }
}
