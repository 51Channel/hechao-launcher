using Hechao.Api.Distribution;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Hechao.Api.Tests;

public sealed class PrivateDownloadRedirectResultTests
{
    [Fact]
    public async Task ExecuteAsync_WritesPrivateHttpsRedirectWithoutBody()
    {
        const string url =
            "https://download.hechao.world/objects/ab/example" +
            "?x-oss-signature=temporary-secret";
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await new PrivateDownloadRedirectResult(url).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(url, context.Response.Headers.Location);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(0, context.Response.ContentLength);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("http://download.hechao.world/object")]
    [InlineData("/relative/object")]
    [InlineData("not a url")]
    public async Task ExecuteAsync_RejectsNonHttpsLocations(string location)
    {
        var context = new DefaultHttpContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PrivateDownloadRedirectResult(location).ExecuteAsync(context));

        Assert.Contains("absolute HTTPS URL", exception.Message);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.Location));
    }
}
