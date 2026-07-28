using System.Diagnostics;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftLaunchBuildSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task HorrorPrankProfile_BuildsFromFormatCharacterDataRoot()
    {
        var dataRoot = Environment.GetEnvironmentVariable(
            "HECHAO_HORROR_PROFILE_SMOKE_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return;
        }

        const string profileId = "pvp-fabric-1.20.1";
        Assert.True(ProfileRuntimePathResolver.ContainsFormatCharacters(dataRoot));

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        var runtimeService = new ProfileJavaRuntimeService(httpClient);
        Assert.True(await runtimeService.IsReadyAsync(dataRoot, profileId));

        var launcher = new MinecraftGameLauncherService(
            httpClient,
            MinecraftServerEndpoint.Parse("127.0.0.1:25589"),
            microsoftClientId: null,
            runtimeRootOverride: null);
        var request = new MinecraftLaunchRequest(
            dataRoot,
            profileId,
            4096,
            new MinecraftLaunchSession(
                "HechaoSmokeTest",
                Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                "not-a-real-minecraft-token",
                DateTimeOffset.UtcNow.AddMinutes(10),
                Xuid: null));

        using var process = await launcher.BuildProcessAsync(request);
        var arguments = GetArguments(process.StartInfo);

        Assert.True(File.Exists(process.StartInfo.FileName));
        Assert.DoesNotContain('\u200c', process.StartInfo.FileName);
        Assert.DoesNotContain('\u200c', arguments);
        Assert.Contains(
            "fabric-loader",
            arguments,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(process.StartInfo.UseShellExecute);
    }

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
        Assert.DoesNotContain('\u200c', GetArguments(process.StartInfo));
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task PerProfileRuntime_StartsJavaFromConfiguredDataRoot()
    {
        var dataRoot = Environment.GetEnvironmentVariable(
            "HECHAO_PROFILE_RUNTIME_START_SMOKE_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
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
        process.StartInfo.FileName = Path.Combine(
            Path.GetDirectoryName(process.StartInfo.FileName)!,
            "java.exe");
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        Assert.True(process.Start());

        var standardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var runningOutput = string.Join(
                Environment.NewLine,
                new[] { await standardError, await standardOutput }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            Assert.DoesNotContain(
                "ClassNotFoundException",
                runningOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Could not find or load main class",
                runningOutput,
                StringComparison.OrdinalIgnoreCase);
            return;
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await standardError, await standardOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var arguments = GetArguments(process.StartInfo);
        var loaderIndex = arguments.IndexOf(
            "fabric-loader-0.19.2.jar",
            StringComparison.OrdinalIgnoreCase);
        var loaderContext = loaderIndex < 0
            ? "<missing>"
            : arguments.Substring(
                Math.Max(0, loaderIndex - 160),
                Math.Min(arguments.Length - Math.Max(0, loaderIndex - 160), 360));
        Assert.Fail(
            $"Java exited before the Minecraft client initialized. Exit code: {process.ExitCode}.{Environment.NewLine}" +
            $"Arguments contain loader: {loaderIndex >= 0}; contain format character: {arguments.Contains('\u200c')}.{Environment.NewLine}" +
            $"Loader context: {loaderContext}{Environment.NewLine}{output}");
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

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsVanillaProcessWithoutStartingIt()
    {
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_VANILLA_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_VANILLA_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
            string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return;
        }

        using var process = await BuildProcessAsync(
            dataRoot,
            runtimeRoot,
            "vanilla-1.21.11");
        var arguments = GetArguments(process.StartInfo);

        Assert.Contains("net.minecraft.client.main.Main", arguments);
        Assert.DoesNotContain(
            "net.fabricmc.loader",
            arguments,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsForgeProcessWithoutStartingIt()
    {
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_FORGE_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_FORGE_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
            string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return;
        }

        using var process = await BuildProcessAsync(
            dataRoot,
            runtimeRoot,
            "forge-1.20.1");
        var arguments = GetArguments(process.StartInfo);

        Assert.Contains("cpw.mods.bootstraplauncher.BootstrapLauncher", arguments);
        Assert.Contains("--fml.forgeVersion", arguments);
        Assert.Contains("47.4.0", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_BuildsDollNightProcessWithoutStartingIt()
    {
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_DOLLNIGHT_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_DOLLNIGHT_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
            string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return;
        }

        using var process = await BuildProcessAsync(
            dataRoot,
            runtimeRoot,
            "dollnight-1.21.11");
        var arguments = GetArguments(process.StartInfo);

        Assert.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", arguments);
        Assert.Contains("fabric-loader-0.19.2", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    private static async Task<Process> BuildProcessAsync(
        string dataRoot,
        string runtimeRoot,
        string profileId)
    {
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
            profileId,
            4096,
            new MinecraftLaunchSession(
                "HechaoSmokeTest",
                Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                "not-a-real-minecraft-token",
                DateTimeOffset.UtcNow.AddMinutes(10),
                Xuid: null));

        return await launcher.BuildProcessAsync(
            request,
            cancellationToken: CancellationToken.None);
    }

    private static string GetArguments(ProcessStartInfo startInfo)
    {
        return startInfo.ArgumentList.Count > 0
            ? string.Join(' ', startInfo.ArgumentList)
            : startInfo.Arguments;
    }
}
