using Hechao.Api.Admin;
using Microsoft.AspNetCore.Http;

namespace Hechao.Api.Tests;

public sealed class AdminWebCanonicalPathMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RedirectsOnlyRootWithoutTrailingSlash()
    {
        var nextCalled = false;
        var middleware = new AdminWebCanonicalPathMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/admin";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/admin/", context.Response.Headers.Location);
    }

    [Theory]
    [InlineData("/admin/")]
    [InlineData("/admin/assets/admin.css")]
    [InlineData("/v1/admin/catalog/servers")]
    [InlineData("/")]
    public async Task InvokeAsync_PassesEveryOtherPathToNext(string path)
    {
        var nextCalled = false;
        var middleware = new AdminWebCanonicalPathMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }
}
