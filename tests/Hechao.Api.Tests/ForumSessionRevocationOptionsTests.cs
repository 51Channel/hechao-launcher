using Hechao.Api.Authentication;

namespace Hechao.Api.Tests;

public sealed class ForumSessionRevocationOptionsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:3000", "http://127.0.0.1:3000/")]
    [InlineData("http://localhost:3000/", "http://localhost:3000/")]
    [InlineData("http://[::1]:3000", "http://[::1]:3000/")]
    public void TryGetBaseUri_AcceptsLoopbackHttpOrigins(
        string value,
        string expected)
    {
        var options = new ForumSessionRevocationOptions { BaseUrl = value };

        Assert.True(options.TryGetBaseUri(out var result));
        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://127.0.0.1:3000")]
    [InlineData("http://example.com:3000")]
    [InlineData("http://127.0.0.1:3000/internal")]
    [InlineData("http://127.0.0.1:3000/?token=value")]
    [InlineData("not-a-uri")]
    public void TryGetBaseUri_RejectsNonLoopbackOrNonOriginValues(string value)
    {
        var options = new ForumSessionRevocationOptions { BaseUrl = value };

        Assert.False(options.TryGetBaseUri(out _));
    }

    [Fact]
    public void HasValidToken_RequiresLongNonWhitespaceSecret()
    {
        Assert.True(new ForumSessionRevocationOptions
        {
            InternalToken = new string('a', 64)
        }.HasValidToken());
        Assert.False(new ForumSessionRevocationOptions
        {
            InternalToken = "too-short"
        }.HasValidToken());
        Assert.False(new ForumSessionRevocationOptions
        {
            InternalToken = new string('a', 32) + "\n"
        }.HasValidToken());
    }
}
