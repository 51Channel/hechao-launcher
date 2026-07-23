using Hechao.Api.Admin;
using Microsoft.AspNetCore.Http;

namespace Hechao.Api.Tests;

public sealed class AdminWebOptionsTests
{
    [Theory]
    [InlineData("https://admin.hechao.world", "https://admin.hechao.world/")]
    [InlineData("https://admin.hechao.world:8443/", "https://admin.hechao.world:8443/")]
    [InlineData("http://127.0.0.1:8090", "http://127.0.0.1:8090/")]
    public void TryGetPublicBaseUri_AcceptsHttpsOrLoopbackOrigins(
        string value,
        string expected)
    {
        var options = new AdminWebOptions { PublicBaseUrl = value };

        Assert.True(options.TryGetPublicBaseUri(out var result));
        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://admin.hechao.world")]
    [InlineData("ftp://127.0.0.1")]
    [InlineData("https://admin.hechao.world/path")]
    [InlineData("https://user@admin.hechao.world")]
    [InlineData("https://admin.hechao.world?mode=test")]
    [InlineData("https://admin.hechao.world/#fragment")]
    [InlineData("not-a-uri")]
    public void TryGetPublicBaseUri_RejectsNonOriginOrInsecureValues(string value)
    {
        var options = new AdminWebOptions { PublicBaseUrl = value };

        Assert.False(options.TryGetPublicBaseUri(out _));
    }

    [Theory]
    [InlineData("admin.hechao.world", true)]
    [InlineData("ADMIN.HECHAO.WORLD", true)]
    [InlineData("admin.hechao.world:443", false)]
    [InlineData("launcher-api.hechao.world", false)]
    public void IsExpectedHost_RequiresConfiguredAuthority(
        string host,
        bool expected)
    {
        var options = new AdminWebOptions
        {
            PublicBaseUrl = "https://admin.hechao.world"
        };

        Assert.Equal(expected, options.IsExpectedHost(new HostString(host)));
    }
}
