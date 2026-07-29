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
        Assert.DoesNotContain('\u200c', process.StartInfo.WorkingDirectory);

        var arguments = GetArguments(process.StartInfo);
        var launchNativeDirectory = MinecraftGameLauncherService
            .ValidateNativeLibraryDirectory(process.StartInfo);
        Assert.DoesNotContain('\u200c', arguments);
        Assert.DoesNotContain("runtime-links", process.StartInfo.WorkingDirectory);
        Assert.Contains("-Djava.library.path=", arguments);
        Assert.Contains("runtime-links", launchNativeDirectory);
        Assert.True(Directory.Exists(launchNativeDirectory));
        Assert.Contains("net.neoforged.fml.startup.Client", arguments);
        Assert.Contains("--fml.neoForgeVersion", arguments);
        Assert.Contains("21.11.42", arguments);
        Assert.Contains("--quickPlayMultiplayer", arguments);
        Assert.Contains("mc.hehe11.fun", arguments);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BuildProcessAsync_LaunchesNeoForgeBeyondEarlyBootstrap()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HECHAO_NEOFORGE_RUNTIME_LAUNCH_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_NEOFORGE_SMOKE_DATA_ROOT");
        var runtimeRoot = Environment.GetEnvironmentVariable("HECHAO_NEOFORGE_SMOKE_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) ||
            string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return;
        }

        using var process = await BuildProcessAsync(
            dataRoot,
            runtimeRoot,
            "activity-neoforge-1.21.11");
        var launchJavaPath = process.StartInfo.FileName;
        var javaDirectory = Path.GetDirectoryName(launchJavaPath);
        Assert.False(string.IsNullOrWhiteSpace(javaDirectory));
        Assert.True(File.Exists(launchJavaPath));

        var logPath = Path.Combine(
            dataRoot,
            "instances",
            "activity-neoforge-1.21.11",
            ".minecraft",
            "logs",
            "latest.log");
        var launchedAt = DateTime.UtcNow;
        try
        {
            Assert.True(process.Start());

            string currentLog = string.Empty;
            var bootstrapped = false;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    Assert.Fail(
                        $"NeoForge exited during early bootstrap with code {process.ExitCode}." +
                        $"{Environment.NewLine}" +
                        string.Join(
                            Environment.NewLine,
                            currentLog
                                .Split(Environment.NewLine)
                                .TakeLast(80)));
                }

                if (File.Exists(logPath) &&
                    File.GetLastWriteTimeUtc(logPath) >= launchedAt.AddSeconds(-2))
                {
                    currentLog = await ReadSharedTextAsync(logPath);
                    Assert.DoesNotContain("FileAlreadyExistsException", currentLog);
                    Assert.DoesNotContain("Unable to create native temp", currentLog);
                    if (currentLog.Contains(
                            "[Meccha Chameleon] Loading",
                            StringComparison.Ordinal))
                    {
                        bootstrapped = true;
                        break;
                    }
                }

                await Task.Delay(250);
            }

            Assert.True(
                bootstrapped,
                $"NeoForge did not pass mod discovery within 90 seconds.{Environment.NewLine}" +
                string.Join(
                    Environment.NewLine,
                    currentLog
                        .Split(Environment.NewLine)
                        .TakeLast(80)));

            var properties = await ReadJavaSystemPropertiesAsync(
                launchJavaPath,
                process.Id);
            var userDirectory = properties
                .Split(Environment.NewLine)
                .FirstOrDefault(line => line.StartsWith("user.dir=", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(userDirectory));
            Assert.DoesNotContain('\u200c', userDirectory);
            Assert.DoesNotContain("runtime-links", userDirectory);
            Assert.Matches(@"~\d", userDirectory);

            var nativeProperties = new[]
            {
                "java.library.path=",
                "org.lwjgl.librarypath=",
                "jna.tmpdir=",
                "org.lwjgl.system.SharedLibraryExtractPath=",
                "io.netty.native.workdir="
            };
            foreach (var propertyName in nativeProperties)
            {
                var property = properties
                    .Split(Environment.NewLine)
                    .FirstOrDefault(line =>
                        line.StartsWith(propertyName, StringComparison.Ordinal));
                Assert.False(string.IsNullOrWhiteSpace(property));
                Assert.DoesNotContain('\u200c', property);
                Assert.Contains("runtime-links", property);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public void NormalizeLaunchGameDirectory_RewritesWorkingDirectoryAndArguments()
    {
        const string gameDirectory = "H:\\hechao \u200cLauncher\\instances\\activity\\.minecraft";
        const string launchGameDirectory = @"H:\HECHAO~2\INSTAN~1\ACTIVI~1\MINECR~1";
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = gameDirectory
        };
        startInfo.ArgumentList.Add($"-DlibraryDirectory={gameDirectory}\\libraries");
        startInfo.ArgumentList.Add("--gameDir");
        startInfo.ArgumentList.Add(gameDirectory);
        startInfo.ArgumentList.Add(
            $"-Dforward.path={gameDirectory.Replace('\\', '/')}/config");

        MinecraftGameLauncherService.NormalizeLaunchGameDirectory(
            startInfo,
            gameDirectory,
            launchGameDirectory);

        Assert.Equal(launchGameDirectory, startInfo.WorkingDirectory);
        Assert.All(
            startInfo.ArgumentList,
            argument =>
            {
                Assert.DoesNotContain('\u200c', argument);
                Assert.DoesNotContain(gameDirectory, argument, StringComparison.OrdinalIgnoreCase);
            });
        Assert.Contains(
            $"-DlibraryDirectory={launchGameDirectory}\\libraries",
            startInfo.ArgumentList);
        Assert.Contains(launchGameDirectory, startInfo.ArgumentList);
        Assert.Contains(
            $"-Dforward.path={launchGameDirectory.Replace('\\', '/')}/config",
            startInfo.ArgumentList);
    }

    [Fact]
    public void NormalizeNativeLibraryDirectory_CanonicalizesAllNativeDirectoryProperties()
    {
        const string launchNativeDirectory =
            @"C:\Users\Player\AppData\Local\Hechao\Launcher\runtime-links\activity-natives";
        var startInfo = new ProcessStartInfo();
        startInfo.ArgumentList.Add("-Djava.library.path=relative-natives");
        startInfo.ArgumentList.Add(
            "H:\\hechao \u200cLauncher\\instances\\activity\\.minecraft\\versions\\neoforge\\natives");
        startInfo.ArgumentList.Add("-Djava.library.path=duplicate-natives");
        startInfo.ArgumentList.Add(
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=generated-natives");
        startInfo.ArgumentList.Add(
            "-Dio.netty.native.workdir=generated/natives");
        startInfo.ArgumentList.Add("--gameDir=generated-natives");

        MinecraftGameLauncherService.NormalizeNativeLibraryDirectory(
            startInfo,
            launchNativeDirectory);

        Assert.Equal(launchNativeDirectory, MinecraftGameLauncherService
            .ValidateNativeLibraryDirectory(startInfo));
        Assert.Equal(
            [
                $"-Djava.library.path={launchNativeDirectory}",
                $"-Dorg.lwjgl.librarypath={launchNativeDirectory}",
                $"-Djna.tmpdir={launchNativeDirectory}",
                $"-Dorg.lwjgl.system.SharedLibraryExtractPath={launchNativeDirectory}",
                $"-Dio.netty.native.workdir={launchNativeDirectory}"
            ],
            startInfo.ArgumentList.Take(5));
        Assert.Single(
            startInfo.ArgumentList,
            argument => argument.StartsWith(
                "-Djava.library.path=",
                StringComparison.Ordinal));
        Assert.Contains("--gameDir=generated-natives", startInfo.ArgumentList);
    }

    [Fact]
    public void NormalizeNativeLibraryDirectory_CanonicalizesPackedArguments()
    {
        const string launchNativeDirectory =
            @"C:\Users\Player Name\AppData\Local\Hechao\Launcher\runtime-links\activity-natives";
        var startInfo = new ProcessStartInfo
        {
            Arguments =
                "-Xmx4G " +
                "-Djava.library.path=\"H:\\hechao \u200cLauncher\\natives\" " +
                "\"-Djna.tmpdir=H:\\hechao \u200cLauncher\\natives\" " +
                "-Djava.library.path=duplicate-natives " +
                "net.neoforged.fml.startup.Client"
        };

        MinecraftGameLauncherService.NormalizeNativeLibraryDirectory(
            startInfo,
            launchNativeDirectory);

        Assert.Equal(launchNativeDirectory, MinecraftGameLauncherService
            .ValidateNativeLibraryDirectory(startInfo));
        Assert.DoesNotContain('\u200c', startInfo.Arguments);
        Assert.Contains(
            $"-Dorg.lwjgl.librarypath=\"{launchNativeDirectory}\"",
            startInfo.Arguments);
        Assert.Contains("-Xmx4G", startInfo.Arguments);
        Assert.EndsWith(
            "net.neoforged.fml.startup.Client",
            startInfo.Arguments,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateNativeLibraryDirectory_RejectsDivergentNativePath()
    {
        var startInfo = new ProcessStartInfo();
        startInfo.ArgumentList.Add("-Djava.library.path=C:\\safe-a");
        startInfo.ArgumentList.Add("-Dorg.lwjgl.librarypath=C:\\safe-b");
        startInfo.ArgumentList.Add("-Djna.tmpdir=C:\\safe-a");
        startInfo.ArgumentList.Add(
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=C:\\safe-a");
        startInfo.ArgumentList.Add("-Dio.netty.native.workdir=C:\\safe-a");

        var exception = Assert.Throws<InvalidDataException>(() =>
            MinecraftGameLauncherService.ValidateNativeLibraryDirectory(startInfo));

        Assert.Contains("same safe path", exception.Message);
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

    private static async Task<string> ReadJavaSystemPropertiesAsync(
        string javaPath,
        int processId)
    {
        var javaDirectory = Path.GetDirectoryName(javaPath) ??
            throw new InvalidDataException("The Java directory is unavailable.");
        var jcmdPath = Path.Combine(javaDirectory, "jcmd.exe");
        Assert.True(File.Exists(jcmdPath));

        using var jcmd = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = jcmdPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        jcmd.StartInfo.ArgumentList.Add(processId.ToString());
        jcmd.StartInfo.ArgumentList.Add("VM.system_properties");

        Assert.True(jcmd.Start());
        var outputTask = jcmd.StandardOutput.ReadToEndAsync();
        var errorTask = jcmd.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await jcmd.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(
            jcmd.ExitCode == 0,
            $"jcmd exited with code {jcmd.ExitCode}: {error}");
        return output;
    }

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
