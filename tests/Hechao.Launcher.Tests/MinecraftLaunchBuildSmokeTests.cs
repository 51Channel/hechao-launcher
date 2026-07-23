using System.Diagnostics;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftLaunchBuildSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsFabricProcessWithoutStartingIt()
    {
        var instancesRoot = Environment.GetEnvironmentVariable("HECHAO_SMOKE_INSTANCES_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(instancesRoot) ||
            string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return;
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        var launcher = new MinecraftGameLauncherService(
            httpClient,
            MinecraftServerEndpoint.Parse("mc.hehe11.fun"),
            microsoftClientId: null,
            runtimeRoot);
        var request = new MinecraftLaunchRequest(
            instancesRoot,
            "base-1.21.11",
            4096,
            new MinecraftLaunchSession(
                "HechaoSmokeTest",
                Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                "not-a-real-minecraft-token",
                DateTimeOffset.UtcNow.AddMinutes(10),
                Xuid: null));

        using var process = await launcher.BuildProcessAsync(
            request,
            cancellationToken: CancellationToken.None);

        Assert.False(process.StartInfo.UseShellExecute);
        Assert.True(File.Exists(process.StartInfo.FileName));

        var arguments = GetArguments(process.StartInfo);
        Assert.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    private static string GetArguments(ProcessStartInfo startInfo)
    {
        return startInfo.ArgumentList.Count > 0
            ? string.Join(' ', startInfo.ArgumentList)
            : startInfo.Arguments;
    }
}
