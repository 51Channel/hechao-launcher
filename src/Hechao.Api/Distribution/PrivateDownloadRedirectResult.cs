using Microsoft.Net.Http.Headers;

namespace Hechao.Api.Distribution;

internal sealed class PrivateDownloadRedirectResult(string location) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Private download redirects require an absolute HTTPS URL.");
        }

        httpContext.Response.StatusCode = StatusCodes.Status302Found;
        httpContext.Response.Headers[HeaderNames.Location] = uri.AbsoluteUri;
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        httpContext.Response.ContentLength = 0;
        return Task.CompletedTask;
    }
}
