using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherApiClientTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateSocketsHttpHandler_UsesConfiguredProxyMode(bool useSystemProxy)
    {
        using var handler = LauncherApiClient.CreateSocketsHttpHandler(useSystemProxy);

        Assert.Equal(useSystemProxy, handler.UseProxy);
    }
}
