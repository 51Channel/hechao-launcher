using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Files;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;
using CmlLib.Core.VersionLoader;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public enum MinecraftLaunchPhase
{
    LoadingProfile,
    PreparingRuntime,
    BuildingProcess,
    Authorizing,
    Starting
}

public sealed record MinecraftLaunchProgress(
    MinecraftLaunchPhase Phase,
    double Percent);

public sealed record MinecraftLaunchRequest(
    string DataRoot,
    string ProfileId,
    int MaximumRamMb,
    MinecraftLaunchSession Session,
    string? JavaExecutablePath = null,
    string? ServerId = null);

public sealed record MinecraftLaunchResult(int ProcessId);

public enum MinecraftProcessExitKind
{
    Natural,
    Requested,
    Forced
}

public sealed record MinecraftProcessExitedEventArgs(
    string ProfileId,
    int ProcessId,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset ExitedAt,
    MinecraftProcessExitKind ExitKind = MinecraftProcessExitKind.Natural,
    string? DataRoot = null);

public sealed record MinecraftRunningGame(
    string ProfileId,
    string? ServerId,
    int ProcessId,
    DateTimeOffset StartedAt,
    string? DataRoot = null);

public enum MinecraftStopPhase
{
    RequestingExit,
    WaitingForExit,
    ForcingExit,
    Complete
}

public sealed record MinecraftStopProgress(MinecraftStopPhase Phase);

public enum MinecraftStopOutcome
{
    NotRunning,
    Graceful,
    Forced
}

public sealed record MinecraftStopResult(MinecraftStopOutcome Outcome);

public interface IMinecraftGameLauncherService
{
    event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited;

    bool IsProfileRunning(string profileId);

    MinecraftRunningGame? GetRunningGame();

    Task<MinecraftStopResult> StopRunningGameAsync(
        TimeSpan gracefulTimeout,
        IProgress<MinecraftStopProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchRequest request,
        IProgress<MinecraftLaunchProgress>? progress = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default);
}

public sealed class MinecraftGameLauncherService : IMinecraftGameLauncherService
{
    private const string ProfileMetadataFileName = "hechao-profile.json";
    private const int MaximumMetadataBytes = 16 * 1024;
    private const int MaximumVersionJsonBytes = 4 * 1024 * 1024;
    private const int MaximumLoggingConfigurationBytes = 2 * 1024 * 1024;
    private const string DefaultServerEndpoint = "mc.hehe11.fun";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MinecraftServerEndpoint _serverEndpoint;
    private readonly string? _microsoftClientId;
    private readonly string? _runtimeRootOverride;
    private readonly IMinecraftRunningStateStore _runningStateStore;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TrackedMinecraftProcess> _runningProcesses =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, MinecraftProcessExitKind> _requestedStops = new();

    public event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited;

    internal MinecraftGameLauncherService(
        HttpClient httpClient,
        MinecraftServerEndpoint serverEndpoint,
        string? microsoftClientId,
        string? runtimeRootOverride,
        IMinecraftRunningStateStore? runningStateStore = null)
    {
        _httpClient = httpClient;
        _serverEndpoint = serverEndpoint;
        _microsoftClientId = microsoftClientId;
        _runtimeRootOverride = string.IsNullOrWhiteSpace(runtimeRootOverride)
            ? null
            : Path.GetFullPath(runtimeRootOverride);
        _runningStateStore =
            runningStateStore ?? NullMinecraftRunningStateStore.Instance;
        TryAttachPersistedProcess();
    }

    public static MinecraftGameLauncherService CreateDefault(
        string? microsoftClientId,
        bool useSystemProxy = false)
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("HECHAO_MINECRAFT_SERVER_ENDPOINT");
        var serverEndpoint = MinecraftServerEndpoint.Parse(
            string.IsNullOrWhiteSpace(configuredEndpoint) ? DefaultServerEndpoint : configuredEndpoint);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = useSystemProxy
        };
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        return new MinecraftGameLauncherService(
            httpClient,
            serverEndpoint,
            microsoftClientId,
            runtimeRootOverride: null,
            JsonMinecraftRunningStateStore.CreateDefault());
    }

    public bool IsProfileRunning(string profileId)
    {
        ManifestValidator.ValidateProfileId(profileId);
        RemoveExitedProcess(profileId);
        return _runningProcesses.ContainsKey(profileId);
    }

    public MinecraftRunningGame? GetRunningGame()
    {
        RemoveExitedProcesses();
        var entry = _runningProcesses
            .OrderBy(pair => pair.Value.StartedAt)
            .FirstOrDefault();
        if (entry.Value is null)
        {
            return null;
        }

        return new MinecraftRunningGame(
            entry.Key,
            entry.Value.ServerId,
            entry.Value.ProcessId,
            entry.Value.StartedAt,
            entry.Value.DataRoot);
    }

    public async Task<MinecraftStopResult> StopRunningGameAsync(
        TimeSpan gracefulTimeout,
        IProgress<MinecraftStopProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (gracefulTimeout < TimeSpan.FromSeconds(1) ||
            gracefulTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gracefulTimeout),
                gracefulTimeout,
                "The graceful Minecraft exit timeout must be between 1 second and 2 minutes.");
        }

        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            RemoveExitedProcesses();
            var running = _runningProcesses.Values.ToArray();
            if (running.Length == 0)
            {
                return new MinecraftStopResult(MinecraftStopOutcome.NotRunning);
            }

            progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.RequestingExit));
            foreach (var tracked in running)
            {
                MarkStopRequested(tracked, MinecraftProcessExitKind.Requested);
                TryCloseMainWindow(tracked.Process);
            }

            progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.WaitingForExit));
            if (await WaitForAllProcessesToExitAsync(
                    gracefulTimeout,
                    cancellationToken))
            {
                progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.Complete));
                return new MinecraftStopResult(MinecraftStopOutcome.Graceful);
            }

            progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.ForcingExit));
            foreach (var tracked in _runningProcesses.Values.ToArray())
            {
                MarkStopRequested(tracked, MinecraftProcessExitKind.Forced);
                TryKillProcess(tracked.Process);
            }

            if (!await WaitForAllProcessesToExitAsync(
                    TimeSpan.FromSeconds(10),
                    cancellationToken))
            {
                throw new MinecraftProcessStopException(
                    "Minecraft did not exit after the launcher ended its process.");
            }

            progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.Complete));
            return new MinecraftStopResult(MinecraftStopOutcome.Forced);
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public async Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchRequest request,
        IProgress<MinecraftLaunchProgress>? progress = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            RemoveExitedProcesses();
            var existing = _runningProcesses.Keys
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            if (existing is not null)
            {
                throw new MinecraftAlreadyRunningException(existing);
            }

            var process = await BuildProcessAsync(request, progress, cancellationToken);
            if (beforeStart is not null)
            {
                try
                {
                    progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.Authorizing, 97));
                    await beforeStart(cancellationToken);
                }
                catch
                {
                    process.Dispose();
                    throw;
                }
            }

            try
            {
                progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.Starting, 100));
                ValidateNativeLibraryDirectory(process.StartInfo);
                if (!process.Start())
                {
                    throw new InvalidOperationException("The Minecraft process did not start.");
                }

                var processId = process.Id;
                var startedAt = GetProcessStartedAt(process);
                var executablePath = GetProcessExecutablePath(
                    process,
                    process.StartInfo.FileName);
                var tracked = new TrackedMinecraftProcess(
                    process,
                    processId,
                    request.ServerId,
                    executablePath,
                    startedAt,
                    Path.GetFullPath(request.DataRoot));
                if (!_runningProcesses.TryAdd(request.ProfileId, tracked))
                {
                    TryKillProcess(process);
                    process.Dispose();
                    throw new MinecraftAlreadyRunningException(request.ProfileId);
                }

                _runningStateStore.Save(new PersistedMinecraftProcess(
                    request.ProfileId,
                    request.ServerId,
                    processId,
                    executablePath,
                    startedAt,
                    Path.GetFullPath(request.DataRoot)));
                process.Exited += (_, _) => HandleProcessExited(
                    request.ProfileId,
                    tracked);
                process.EnableRaisingEvents = true;
                return new MinecraftLaunchResult(processId);
            }
            catch (MinecraftAlreadyRunningException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _runningProcesses.TryRemove(request.ProfileId, out _);
                TryKillProcess(process);
                TryClearPersistedProcess(process);
                process.Dispose();
                throw new MinecraftLaunchException(
                    MinecraftLaunchFailure.ProcessStart,
                    "Unable to start the Minecraft process.",
                    exception);
            }
        }
        finally
        {
            _launchGate.Release();
        }
    }

    internal async Task<Process> BuildProcessAsync(
        MinecraftLaunchRequest request,
        IProgress<MinecraftLaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.LoadingProfile, 2));

        string gameDirectory;
        string launchGameDirectory;
        string runtimeRoot;
        string launchRuntimeRoot;
        string? customJavaPath = null;
        MinecraftProfileMetadata metadata;
        try
        {
            var layout = new ClientStorageLayout(request.DataRoot);
            gameDirectory = ResolveProfileGameDirectory(layout, request.ProfileId);
            launchGameDirectory = ProfileRuntimePathResolver.GetGameLaunchRoot(gameDirectory);
            runtimeRoot = _runtimeRootOverride ?? layout.GetProfileRuntimeRoot(request.ProfileId);
            launchRuntimeRoot = ProfileRuntimePathResolver.GetRuntimeLaunchRoot(
                runtimeRoot,
                request.ProfileId);
            metadata = await ReadAndValidateMetadataAsync(gameDirectory, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ManifestFormatException)
        {
            throw new MinecraftLaunchException(
                MinecraftLaunchFailure.InvalidProfile,
                "The installed client profile is not launchable.",
                exception);
        }

        if (!string.IsNullOrWhiteSpace(request.JavaExecutablePath))
        {
            try
            {
                customJavaPath = (await JavaRuntimeValidator.ValidateAsync(
                    request.JavaExecutablePath,
                    metadata.JavaMajorVersion,
                    cancellationToken)).ExecutablePath;
            }
            catch (JavaRuntimeValidationException exception)
            {
                throw new MinecraftLaunchException(
                    MinecraftLaunchFailure.InvalidJavaSelection,
                    $"The selected Java runtime is not compatible with Java {metadata.JavaMajorVersion}.",
                    exception);
            }
        }

        Directory.CreateDirectory(runtimeRoot);
        var minecraftPath = new MinecraftPath(launchGameDirectory)
        {
            Runtime = launchRuntimeRoot
        };
        var parameters = MinecraftLauncherParameters.CreateDefault(minecraftPath, _httpClient);
        parameters.VersionLoader = new LocalJsonVersionLoader(minecraftPath);

        var javaPathResolver = parameters.JavaPathResolver ??
            throw new InvalidOperationException("The Java path resolver is unavailable.");
        if (OperatingSystem.IsMacOS())
        {
            // The signed profile still owns the version JSON, mods and configuration.
            // Complete only platform-selected Mojang files so a profile authored on
            // Windows can acquire macOS ARM64 natives without weakening manifest trust.
            var platformExtractors = DefaultFileExtractors.CreateDefault(
                _httpClient,
                parameters.RulesEvaluator ??
                    throw new InvalidOperationException(
                        "The Minecraft rules evaluator is unavailable."),
                javaPathResolver);
            platformExtractors.Client = null;
            parameters.FileExtractors = platformExtractors.ToExtractorCollection();
        }
        else
        {
            // Preserve the released Windows behavior: the signed Hechao manifest owns
            // game files and CmlLib only manages Mojang's Java runtime.
            var runtimeExtractors = new FileExtractorCollection();
            runtimeExtractors.Add(new JavaFileExtractor(
                _httpClient,
                javaPathResolver));
            parameters.FileExtractors = runtimeExtractors;
        }

        var launcher = new MinecraftLauncher(parameters);
        var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(value =>
        {
            var ratio = value.TotalTasks <= 0
                ? 0
                : Math.Clamp(value.ProgressedTasks / (double)value.TotalTasks, 0, 1);
            progress?.Report(new MinecraftLaunchProgress(
                MinecraftLaunchPhase.PreparingRuntime,
                5 + ratio * 82));
        });
        var byteProgress = new Progress<ByteProgress>(value =>
        {
            var ratio = value.TotalBytes <= 0
                ? 0
                : Math.Clamp(value.ProgressedBytes / (double)value.TotalBytes, 0, 1);
            progress?.Report(new MinecraftLaunchProgress(
                MinecraftLaunchPhase.PreparingRuntime,
                5 + ratio * 82));
        });

        CmlLib.Core.Version.IVersion version;
        try
        {
            progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.PreparingRuntime, 5));
            version = await launcher.GetVersionAsync(metadata.VersionId, cancellationToken);
            await launcher.InstallAsync(version, fileProgress, byteProgress, cancellationToken);
            await EnsureLoggingConfigurationAsync(
                _httpClient,
                launchGameDirectory,
                version.Logging,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MinecraftLaunchException(
                MinecraftLaunchFailure.RuntimePreparation,
                $"Unable to prepare Java {metadata.JavaMajorVersion}.",
                exception);
        }

        try
        {
            progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.BuildingProcess, 92));
            string launchNativeDirectory;
            try
            {
                var extractedNativeDirectory = launcher.NativeLibraryExtractor.Extract(
                    minecraftPath,
                    version,
                    launcher.RulesContext);
                launchNativeDirectory = await NativeLibraryRunDirectory.PrepareAsync(
                    extractedNativeDirectory,
                    request.ProfileId,
                    metadata.VersionId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MinecraftLaunchException(
                    MinecraftLaunchFailure.NativeLibraryPreparation,
                    "Unable to prepare the Minecraft native libraries.",
                    exception);
            }

            var process = launcher.BuildProcess(version, new MLaunchOption
            {
                Session = new MSession(
                    request.Session.Username,
                    request.Session.AccessToken,
                    request.Session.MinecraftUuid.ToString("N"))
                {
                    UserType = "msa",
                    Xuid = request.Session.Xuid
                },
                MaximumRamMb = request.MaximumRamMb,
                MinimumRamMb = Math.Min(512, request.MaximumRamMb),
                ServerIp = _serverEndpoint.Host,
                ServerPort = _serverEndpoint.Port,
                ClientId = _microsoftClientId,
                NativesDirectory = launchNativeDirectory,
                GameLauncherName = "Hechao Launcher",
                GameLauncherVersion = LauncherProductInfo.Version
            });

            process.StartInfo.UseShellExecute = false;
            NormalizeLaunchGameDirectory(
                process.StartInfo,
                gameDirectory,
                launchGameDirectory);
            NormalizeNativeLibraryDirectory(
                process.StartInfo,
                launchNativeDirectory);

            var javaPath = Path.GetFullPath(process.StartInfo.FileName);
            if (!File.Exists(javaPath) || !IsWithin(launchRuntimeRoot, javaPath))
            {
                process.Dispose();
                throw new InvalidDataException("The resolved Java runtime is outside the managed runtime directory.");
            }

            process.StartInfo.FileName = customJavaPath ??
                ResolveLaunchExecutablePath(javaPath);
            return process;
        }
        catch (Exception exception) when (exception is not MinecraftLaunchException)
        {
            throw new MinecraftLaunchException(
                MinecraftLaunchFailure.ProcessCreation,
                "Unable to build the Minecraft process.",
                exception);
        }
    }

    internal static void NormalizeLaunchGameDirectory(
        ProcessStartInfo startInfo,
        string gameDirectory,
        string launchGameDirectory)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var fullGameDirectory = Path.GetFullPath(gameDirectory);
        startInfo.WorkingDirectory = launchGameDirectory;
        if (startInfo.ArgumentList.Count > 0)
        {
            for (var index = 0; index < startInfo.ArgumentList.Count; index++)
            {
                startInfo.ArgumentList[index] = ReplaceLaunchPath(
                    startInfo.ArgumentList[index],
                    fullGameDirectory,
                    launchGameDirectory);
            }

            return;
        }

        startInfo.Arguments = ReplaceLaunchPath(
            startInfo.Arguments,
            fullGameDirectory,
            launchGameDirectory);
    }

    internal static void NormalizeNativeLibraryDirectory(
        ProcessStartInfo startInfo,
        string launchNativeDirectory)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var fullLaunchNativeDirectory = Path.GetFullPath(launchNativeDirectory);
        if (startInfo.ArgumentList.Count > 0)
        {
            for (var index = startInfo.ArgumentList.Count - 1; index >= 0; index--)
            {
                if (GetNativeDirectoryArgumentPrefix(startInfo.ArgumentList[index]) is null)
                {
                    continue;
                }

                startInfo.ArgumentList.RemoveAt(index);
            }

            for (var index = NativeDirectoryArgumentPrefixes.Length - 1; index >= 0; index--)
            {
                startInfo.ArgumentList.Insert(
                    0,
                    NativeDirectoryArgumentPrefixes[index] + fullLaunchNativeDirectory);
            }
        }
        else
        {
            var remainingArguments = startInfo.Arguments;
            foreach (var prefix in NativeDirectoryArgumentPrefixes)
            {
                remainingArguments = NativeDirectoryArgumentRegex(prefix)
                    .Replace(remainingArguments, string.Empty);
            }

            var nativeArguments = string.Join(
                ' ',
                NativeDirectoryArgumentPrefixes.Select(prefix =>
                    FormatPackedNativeDirectoryArgument(
                        prefix,
                        fullLaunchNativeDirectory)));
            startInfo.Arguments = string.IsNullOrWhiteSpace(remainingArguments)
                ? nativeArguments
                : $"{nativeArguments} {remainingArguments.Trim()}";
        }

        ValidateNativeLibraryDirectory(startInfo);
    }

    internal static string ValidateNativeLibraryDirectory(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        string? expectedDirectory = null;
        foreach (var prefix in NativeDirectoryArgumentPrefixes)
        {
            var values = GetNativeDirectoryArgumentValues(startInfo, prefix);
            if (values.Count != 1)
            {
                throw new InvalidDataException(
                    $"Minecraft must have exactly one {prefix} native directory argument.");
            }

            var directory = Path.GetFullPath(values[0]);
            if (ProfileRuntimePathResolver.ContainsFormatCharacters(directory))
            {
                throw new InvalidDataException(
                    "The Minecraft native directory contains unsupported Unicode format characters.");
            }

            expectedDirectory ??= directory;
            if (!string.Equals(
                    expectedDirectory,
                    directory,
                    GetPathComparison()))
            {
                throw new InvalidDataException(
                    "Minecraft native directory arguments do not resolve to the same safe path.");
            }
        }

        return expectedDirectory!;
    }

    private static string ReplaceLaunchPath(
        string value,
        string gameDirectory,
        string launchGameDirectory)
    {
        var rewritten = value.Replace(
            gameDirectory,
            launchGameDirectory,
            GetPathComparison());
        return rewritten.Replace(
            gameDirectory.Replace('\\', '/'),
            launchGameDirectory.Replace('\\', '/'),
            GetPathComparison());
    }

    private static readonly string[] NativeDirectoryArgumentPrefixes =
    [
        "-Djava.library.path=",
        "-Dorg.lwjgl.librarypath=",
        "-Djna.tmpdir=",
        "-Dorg.lwjgl.system.SharedLibraryExtractPath=",
        "-Dio.netty.native.workdir="
    ];

    private static string? GetNativeDirectoryArgumentPrefix(string argument) =>
        NativeDirectoryArgumentPrefixes.FirstOrDefault(prefix =>
            argument.StartsWith(prefix, StringComparison.Ordinal));

    private static IReadOnlyList<string> GetNativeDirectoryArgumentValues(
        ProcessStartInfo startInfo,
        string prefix)
    {
        if (startInfo.ArgumentList.Count > 0)
        {
            return startInfo.ArgumentList
                .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
                .Select(argument => argument[prefix.Length..])
                .ToArray();
        }

        return NativeDirectoryArgumentRegex(prefix)
            .Matches(startInfo.Arguments)
            .Select(match =>
            {
                var groups = match.Groups;
                if (groups["whole"].Success)
                {
                    return groups["whole"].Value;
                }

                return groups["quoted"].Success
                    ? groups["quoted"].Value
                    : groups["plain"].Value;
            })
            .ToArray();
    }

    private static Regex NativeDirectoryArgumentRegex(string prefix) =>
        new(
            $@"(?<!\S)(?:""{Regex.Escape(prefix)}(?<whole>[^""]*)""|{Regex.Escape(prefix)}(?:""(?<quoted>[^""]*)""|(?<plain>\S+)))",
            RegexOptions.CultureInvariant);

    private static string FormatPackedNativeDirectoryArgument(
        string prefix,
        string directory) =>
        directory.Any(char.IsWhiteSpace)
            ? $"{prefix}\"{directory}\""
            : prefix + directory;

    internal static async Task EnsureLoggingConfigurationAsync(
        HttpClient httpClient,
        string gameDirectory,
        MLogFileMetadata? logging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (logging?.LogFile is not { } metadata)
        {
            return;
        }

        var relativePath = metadata.Path;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = metadata.Name ?? metadata.Id;
        }

        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.IsNullOrWhiteSpace(metadata.Url) ||
            string.IsNullOrWhiteSpace(metadata.Sha1) ||
            metadata.Size is <= 0 or > MaximumLoggingConfigurationBytes)
        {
            throw new InvalidDataException("The Minecraft logging configuration metadata is invalid.");
        }

        var normalizedRelativePath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelativePath))
        {
            throw new InvalidDataException(
                "The Minecraft logging configuration path must be relative.");
        }

        var fullGameDirectory = Path.GetFullPath(gameDirectory);
        var loggingRoot = Path.Combine(fullGameDirectory, "assets", "log_configs");
        var managedPrefix = Path.Combine("assets", "log_configs") +
                            Path.DirectorySeparatorChar;
        string destinationPath;
        if (normalizedRelativePath.Contains(Path.DirectorySeparatorChar))
        {
            if (!normalizedRelativePath.StartsWith(
                    managedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The Minecraft logging configuration path is outside the managed log directory.");
            }

            destinationPath = Path.GetFullPath(
                Path.Combine(fullGameDirectory, normalizedRelativePath));
        }
        else
        {
            destinationPath = Path.GetFullPath(
                Path.Combine(loggingRoot, normalizedRelativePath));
        }

        if (!IsWithin(loggingRoot, destinationPath))
        {
            throw new InvalidDataException(
                "The Minecraft logging configuration path is outside the managed log directory.");
        }

        if (!Uri.TryCreate(metadata.Url, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(sourceUri.UserInfo))
        {
            throw new InvalidDataException("The Minecraft logging configuration URL is invalid.");
        }

        var expectedSha1 = metadata.Sha1.ToLowerInvariant();
        if (expectedSha1.Length != 40 || expectedSha1.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidDataException("The Minecraft logging configuration hash is invalid.");
        }

        if (await IsMatchingLoggingConfigurationAsync(
                destinationPath,
                metadata.Size,
                expectedSha1,
                cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await httpClient.GetAsync(
                sourceUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength != metadata.Size)
            {
                throw new InvalidDataException(
                    "The Minecraft logging configuration length is invalid.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > metadata.Size ||
                    totalBytes > MaximumLoggingConfigurationBytes)
                {
                    throw new InvalidDataException(
                        "The Minecraft logging configuration is larger than expected.");
                }

                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            var actualSha1 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (totalBytes != metadata.Size ||
                !string.Equals(actualSha1, expectedSha1, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Minecraft logging configuration failed integrity verification.");
            }

            destination.Close();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<bool> IsMatchingLoggingConfigurationAsync(
        string path,
        long expectedSize,
        string expectedSha1,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hasher.AppendData(buffer, 0, read);
        }

        var actualSha1 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return string.Equals(actualSha1, expectedSha1, StringComparison.Ordinal);
    }

    private static void ValidateRequest(MinecraftLaunchRequest request)
    {
        ManifestValidator.ValidateProfileId(request.ProfileId);
        if (!string.IsNullOrWhiteSpace(request.ServerId))
        {
            ManifestValidator.ValidateProfileId(request.ServerId);
        }

        if (string.IsNullOrWhiteSpace(request.DataRoot))
        {
            throw new ArgumentException("The client data root is required.", nameof(request));
        }

        if (request.MaximumRamMb is < 1024 or > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaximumRamMb,
                "Minecraft memory must be between 1 GiB and 64 GiB.");
        }

        if (string.IsNullOrWhiteSpace(request.Session.Username) ||
            string.IsNullOrWhiteSpace(request.Session.AccessToken) ||
            request.Session.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            throw new MinecraftLaunchSessionExpiredException();
        }
    }

    internal static string ResolveLaunchExecutablePath(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            return fullPath;
        }

        var buffer = new StringBuilder(WindowsMaximumPath);
        var length = GetShortPathName(fullPath, buffer, (uint)buffer.Capacity);
        return length is > 0 and < WindowsMaximumPath
            ? buffer.ToString()
            : fullPath;
    }

    private const int WindowsMaximumPath = 32_768;

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetShortPathNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetShortPathName(
        string longPath,
        StringBuilder shortPath,
        uint bufferLength);

    private static string ResolveProfileGameDirectory(
        ClientStorageLayout layout,
        string profileId)
    {
        var profileDirectory = layout.GetProfileRoot(profileId);
        var gameDirectory = layout.GetProfileGameDirectory(profileId);
        if (!Directory.Exists(profileDirectory))
        {
            throw new DirectoryNotFoundException(profileDirectory);
        }

        EnsureDirectoryIsNotReparsePoint(profileDirectory);
        if (!Directory.Exists(gameDirectory))
        {
            throw new DirectoryNotFoundException(gameDirectory);
        }

        EnsureDirectoryIsNotReparsePoint(gameDirectory);
        return gameDirectory;
    }

    internal static async Task<MinecraftProfileMetadata> ReadAndValidateMetadataAsync(
        string profileDirectory,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(profileDirectory, ProfileMetadataFileName);
        var metadataFile = new FileInfo(metadataPath);
        if (!metadataFile.Exists || metadataFile.Length is <= 0 or > MaximumMetadataBytes)
        {
            throw new InvalidDataException("The client launch metadata is missing or invalid.");
        }

        await using var metadataStream = metadataFile.OpenRead();
        var metadata = await JsonSerializer.DeserializeAsync<MinecraftProfileMetadata>(
            metadataStream,
            SerializerOptions,
            cancellationToken) ?? throw new InvalidDataException("The client launch metadata is empty.");
        if (metadata.SchemaVersion != 1 ||
            metadata.JavaMajorVersion is < 8 or > 99 ||
            !IsSafeVersionId(metadata.VersionId))
        {
            throw new InvalidDataException("The client launch metadata contains invalid values.");
        }

        var versionDirectory = Path.Combine(profileDirectory, "versions", metadata.VersionId);
        EnsureDirectoryIsNotReparsePoint(versionDirectory);
        var versionJsonPath = Path.Combine(versionDirectory, metadata.VersionId + ".json");
        var versionJarPath = Path.Combine(versionDirectory, metadata.VersionId + ".jar");
        var versionJson = new FileInfo(versionJsonPath);
        if (!versionJson.Exists ||
            versionJson.Length is <= 0 or > MaximumVersionJsonBytes ||
            !File.Exists(versionJarPath))
        {
            throw new InvalidDataException("The selected Minecraft version is incomplete.");
        }

        await using var versionStream = versionJson.OpenRead();
        using var document = await JsonDocument.ParseAsync(
            versionStream,
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var versionId = root.TryGetProperty("id", out var idProperty)
            ? idProperty.GetString()
            : null;
        var javaMajorVersion =
            root.TryGetProperty("javaVersion", out var javaVersionProperty) &&
            javaVersionProperty.TryGetProperty("majorVersion", out var majorProperty) &&
            majorProperty.TryGetInt32(out var parsedMajorVersion)
                ? parsedMajorVersion
                : 0;
        if (!string.Equals(versionId, metadata.VersionId, StringComparison.Ordinal) ||
            javaMajorVersion != metadata.JavaMajorVersion)
        {
            throw new InvalidDataException("The client launch metadata does not match the version JSON.");
        }

        return metadata;
    }

    private static bool IsSafeVersionId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= 160 &&
               value is not "." and not ".." &&
               value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !value.EndsWith(' ') &&
               !value.EndsWith('.');
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The client profile contains an invalid directory link.");
        }
    }

    private static bool IsWithin(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, GetPathComparison());
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private void TryAttachPersistedProcess()
    {
        PersistedMinecraftProcess? persisted;
        try
        {
            persisted = _runningStateStore.Load();
        }
        catch
        {
            return;
        }

        if (persisted is null)
        {
            return;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(persisted.ProcessId);
            if (process.HasExited)
            {
                ClearPersistedProcess(persisted.ProcessId, persisted.StartedAt);
                process.Dispose();
                return;
            }

            var startedAt = GetProcessStartedAt(process);
            var executablePath = GetProcessExecutablePath(process);
            if (!JsonMinecraftRunningStateStore.StartedAtMatches(
                    persisted.StartedAt,
                    startedAt) ||
                !string.Equals(
                    Path.GetFullPath(persisted.ExecutablePath),
                    executablePath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                ClearPersistedProcess(persisted.ProcessId, persisted.StartedAt);
                process.Dispose();
                return;
            }

            var tracked = new TrackedMinecraftProcess(
                process,
                persisted.ProcessId,
                persisted.ServerId,
                executablePath,
                startedAt,
                persisted.DataRoot);
            if (!_runningProcesses.TryAdd(persisted.ProfileId, tracked))
            {
                process.Dispose();
                return;
            }

            process.Exited += (_, _) => HandleProcessExited(
                persisted.ProfileId,
                tracked);
            process.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            NotSupportedException or Win32Exception)
        {
            ClearPersistedProcess(persisted.ProcessId, persisted.StartedAt);
            process?.Dispose();
        }
    }

    private void RemoveExitedProcess(string profileId)
    {
        if (!_runningProcesses.TryGetValue(profileId, out var tracked))
        {
            return;
        }

        if (HasExited(tracked.Process))
        {
            HandleProcessExited(profileId, tracked);
        }
    }

    private void RemoveExitedProcesses()
    {
        foreach (var pair in _runningProcesses.ToArray())
        {
            if (HasExited(pair.Value.Process))
            {
                HandleProcessExited(pair.Key, pair.Value);
            }
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    private void HandleProcessExited(
        string profileId,
        TrackedMinecraftProcess tracked)
    {
        if (!tracked.TryBeginExitHandling())
        {
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = tracked.Process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }

        var processId = tracked.ProcessId;
        var exitKind = _requestedStops.TryRemove(processId, out var requestedKind)
            ? requestedKind
            : MinecraftProcessExitKind.Natural;

        RemoveAndDisposeProcess(profileId, tracked);
        ClearPersistedProcess(processId, tracked.StartedAt);
        NotifyProcessExited(new MinecraftProcessExitedEventArgs(
            profileId,
            processId,
            exitCode,
            tracked.StartedAt,
            DateTimeOffset.UtcNow,
            exitKind,
            tracked.DataRoot));
    }

    private static DateTimeOffset GetProcessStartedAt(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or
            Win32Exception)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static string GetProcessExecutablePath(
        Process process,
        string? fallbackPath = null)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return Path.GetFullPath(path);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or
            Win32Exception)
        {
        }

        if (!string.IsNullOrWhiteSpace(fallbackPath))
        {
            return Path.GetFullPath(fallbackPath);
        }

        throw new InvalidOperationException(
            "The Minecraft process executable path could not be determined.");
    }

    private void MarkStopRequested(
        TrackedMinecraftProcess tracked,
        MinecraftProcessExitKind exitKind)
    {
        _requestedStops[tracked.ProcessId] = exitKind;
    }

    private static void TryCloseMainWindow(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                _ = process.CloseMainWindow();
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or
            Win32Exception)
        {
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or
            Win32Exception)
        {
        }
    }

    private async Task<bool> WaitForAllProcessesToExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveExitedProcesses();
            if (_runningProcesses.IsEmpty)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        RemoveExitedProcesses();
        return _runningProcesses.IsEmpty;
    }

    private void TryClearPersistedProcess(Process process)
    {
        try
        {
            ClearPersistedProcess(process.Id, GetProcessStartedAt(process));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ClearPersistedProcess(int processId, DateTimeOffset startedAt)
    {
        try
        {
            _runningStateStore.ClearIfMatches(processId, startedAt);
        }
        catch
        {
            // Process cleanup must not fail because a state file is unavailable.
        }
    }

    private void NotifyProcessExited(MinecraftProcessExitedEventArgs eventArgs)
    {
        var handlers = ProcessExited;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<MinecraftProcessExitedEventArgs> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // A diagnostics subscriber must never affect process cleanup.
            }
        }
    }

    private void RemoveAndDisposeProcess(
        string profileId,
        TrackedMinecraftProcess tracked)
    {
        if (_runningProcesses.TryRemove(
                new KeyValuePair<string, TrackedMinecraftProcess>(
                    profileId,
                    tracked)))
        {
            tracked.Process.Dispose();
        }
    }

    private sealed class TrackedMinecraftProcess(
        Process process,
        int processId,
        string? serverId,
        string executablePath,
        DateTimeOffset startedAt,
        string? dataRoot)
    {
        private int _exitHandled;

        public Process Process { get; } = process;

        public int ProcessId { get; } = processId;

        public string? ServerId { get; } = serverId;

        public string ExecutablePath { get; } = executablePath;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public string? DataRoot { get; } = dataRoot;

        public bool TryBeginExitHandling() =>
            Interlocked.Exchange(ref _exitHandled, 1) == 0;
    }
}

internal sealed record MinecraftProfileMetadata(
    int SchemaVersion,
    string VersionId,
    int JavaMajorVersion);

public sealed record MinecraftServerEndpoint(string Host, int Port)
{
    public static MinecraftServerEndpoint Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
        }

        var input = value.Trim();
        if (input.Any(char.IsControl) ||
            input.Any(char.IsWhiteSpace) ||
            input.IndexOfAny(['/', '\\', '@', '?', '#']) >= 0)
        {
            throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
        }

        var host = input;
        var port = 25565;
        if (input.StartsWith('['))
        {
            var closingBracket = input.IndexOf(']');
            if (closingBracket <= 1)
            {
                throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
            }

            host = input[1..closingBracket];
            var suffix = input[(closingBracket + 1)..];
            if (suffix.Length > 0 &&
                (suffix[0] != ':' || !TryParsePort(suffix[1..], out port)))
            {
                throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
            }
        }
        else
        {
            var firstColon = input.IndexOf(':');
            var lastColon = input.LastIndexOf(':');
            if (firstColon > 0 && firstColon == lastColon)
            {
                host = input[..firstColon];
                if (!TryParsePort(input[(firstColon + 1)..], out port))
                {
                    throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
                }
            }
        }

        var hostKind = Uri.CheckHostName(host);
        if (hostKind == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException("HECHAO_MINECRAFT_SERVER_ENDPOINT is invalid.");
        }

        var launchHost = hostKind == UriHostNameType.IPv6 ? $"[{host}]" : host;
        return new MinecraftServerEndpoint(launchHost, port);
    }

    private static bool TryParsePort(string value, out int port)
    {
        return int.TryParse(value, out port) && port is > 0 and <= 65535;
    }
}

public enum MinecraftLaunchFailure
{
    InvalidProfile,
    InvalidJavaSelection,
    RuntimePreparation,
    NativeLibraryPreparation,
    ProcessCreation,
    ProcessStart
}

public sealed class MinecraftLaunchException(
    MinecraftLaunchFailure failure,
    string message,
    Exception innerException) : Exception(message, innerException)
{
    public MinecraftLaunchFailure Failure { get; } = failure;
}

public sealed class MinecraftAlreadyRunningException(string profileId)
    : Exception($"Minecraft profile {profileId} is already running.");

public sealed class MinecraftProcessStopException(string message)
    : Exception(message);

public sealed class MinecraftLaunchSessionExpiredException
    : Exception
{
    public MinecraftLaunchSessionExpiredException()
        : base("The Minecraft launch session has expired.")
    {
    }
}
