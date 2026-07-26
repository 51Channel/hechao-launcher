using System.Diagnostics;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftLaunchBuildSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task PerProfileRuntime_InstallsAndBuildsFromConfiguredDataRoot()
    {
        var dataRoot = Environment.GetEnvironmentVariable(
            "HECHAO_PROFILE_RUNTIME_SMOKE_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return;
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        var runtimeService = new ProfileJavaRuntimeService(httpClient);
        await runtimeService.InstallAsync(
            dataRoot,
            "base-1.21.11");
        Assert.True(await runtimeService.IsReadyAsync(
            dataRoot,
            "base-1.21.11"));

        var launcher = new MinecraftGameLauncherService(
            httpClient,
            MinecraftServerEndpoint.Parse("mc.hehe11.fun"),
            microsoftClientId: null,
            runtimeRootOverride: null);
        var request = new MinecraftLaunchRequest(
            dataRoot,
            "base-1.21.11",
            4096,
            new MinecraftLaunchSession(
                "HechaoSmokeTest",
                Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                "not-a-real-minecraft-token",
                DateTimeOffset.UtcNow.AddMinutes(10),
                Xuid: null));

        using var process = await launcher.BuildProcessAsync(request);

        Assert.True(File.Exists(process.StartInfo.FileName));
        Assert.DoesNotContain('\u200c', process.StartInfo.FileName);
        Assert.EndsWith(
            "javaw.exe",
            process.StartInfo.FileName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsFabricProcessWithoutStartingIt()
    {
        var dataRoot =
            Environment.GetEnvironmentVariable("HECHAO_SMOKE_DATA_ROOT") ??
            Environment.GetEnvironmentVariable("HECHAO_SMOKE_INSTANCES_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
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
            dataRoot,
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
        Assert.DoesNotContain('\u200c', process.StartInfo.FileName);

        var arguments = GetArguments(process.StartInfo);
        Assert.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsNeoForgeProcessWithoutStartingIt()
    {
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_NEOFORGE_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_NEOFORGE_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
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
            dataRoot,
            "activity-neoforge-1.21.11",
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
        Assert.DoesNotContain('\u200c', process.StartInfo.FileName);

        var arguments = GetArguments(process.StartInfo);
        Assert.Contains("net.neoforged.fml.startup.Client", arguments);
        Assert.Contains("--fml.neoForgeVersion", arguments);
        Assert.Contains("21.11.42", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsPvpFabricProcessWithoutStartingIt()
    {
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_PVP_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_PVP_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
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
            dataRoot,
            "pvp-fabric-1.20.1",
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
        Assert.DoesNotContain('\u200c', process.StartInfo.FileName);

        var arguments = GetArguments(process.StartInfo);
        Assert.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", arguments);
        Assert.Contains("fabric-loader-0.16.14", arguments);
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
