using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.ProcessBuilder;
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
    string InstancesRoot,
    string ProfileId,
    int MaximumRamMb,
    MinecraftLaunchSession Session);

public sealed record MinecraftLaunchResult(int ProcessId);

public interface IMinecraftGameLauncherService
{
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
    private const string DefaultServerEndpoint = "mc.hehe11.fun";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MinecraftServerEndpoint _serverEndpoint;
    private readonly string? _microsoftClientId;
    private readonly string _runtimeRoot;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Process> _runningProcesses =
        new(StringComparer.Ordinal);

    internal MinecraftGameLauncherService(
        HttpClient httpClient,
        MinecraftServerEndpoint serverEndpoint,
        string? microsoftClientId,
        string runtimeRoot)
    {
        _httpClient = httpClient;
        _serverEndpoint = serverEndpoint;
        _microsoftClientId = microsoftClientId;
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
    }

    public static MinecraftGameLauncherService CreateDefault(string? microsoftClientId)
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("HECHAO_MINECRAFT_SERVER_ENDPOINT");
        var serverEndpoint = MinecraftServerEndpoint.Parse(
            string.IsNullOrWhiteSpace(configuredEndpoint) ? DefaultServerEndpoint : configuredEndpoint);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var runtimeRoot = Path.Combine(localApplicationData, "Hechao", "Launcher", "runtime");
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
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
            runtimeRoot);
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
            RemoveExitedProcess(request.ProfileId);
            if (_runningProcesses.ContainsKey(request.ProfileId))
            {
                throw new MinecraftAlreadyRunningException(request.ProfileId);
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

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => RemoveAndDisposeProcess(request.ProfileId, process);
            if (!_runningProcesses.TryAdd(request.ProfileId, process))
            {
                process.Dispose();
                throw new MinecraftAlreadyRunningException(request.ProfileId);
            }

            try
            {
                progress?.Report(new MinecraftLaunchProgress(MinecraftLaunchPhase.Starting, 100));
                if (!process.Start())
                {
                    throw new InvalidOperationException("The Minecraft process did not start.");
                }

                return new MinecraftLaunchResult(process.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _runningProcesses.TryRemove(request.ProfileId, out _);
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

        string profileDirectory;
        MinecraftProfileMetadata metadata;
        try
        {
            profileDirectory = ResolveProfileDirectory(request.InstancesRoot, request.ProfileId);
            metadata = await ReadAndValidateMetadataAsync(profileDirectory, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ManifestFormatException)
        {
            throw new MinecraftLaunchException(
                MinecraftLaunchFailure.InvalidProfile,
                "The installed client profile is not launchable.",
                exception);
        }

        Directory.CreateDirectory(_runtimeRoot);
        var minecraftPath = new MinecraftPath(profileDirectory)
        {
            Runtime = _runtimeRoot
        };
        var parameters = MinecraftLauncherParameters.CreateDefault(minecraftPath, _httpClient);
        parameters.VersionLoader = new LocalJsonVersionLoader(minecraftPath);

        // The signed Hechao manifest owns game files. CmlLib only manages Mojang's Java runtime here.
        var runtimeExtractors = new FileExtractorCollection();
        runtimeExtractors.Add(new JavaFileExtractor(
            _httpClient,
            parameters.JavaPathResolver ??
            throw new InvalidOperationException("The Java path resolver is unavailable.")));
        parameters.FileExtractors = runtimeExtractors;

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
                GameLauncherName = "Hechao Launcher",
                GameLauncherVersion = LauncherProductInfo.Version
            });
            process.StartInfo.UseShellExecute = false;

            var javaPath = Path.GetFullPath(process.StartInfo.FileName);
            if (!File.Exists(javaPath) || !IsWithin(_runtimeRoot, javaPath))
            {
                process.Dispose();
                throw new InvalidDataException("The resolved Java runtime is outside the managed runtime directory.");
            }

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

    private static void ValidateRequest(MinecraftLaunchRequest request)
    {
        ManifestValidator.ValidateProfileId(request.ProfileId);
        if (string.IsNullOrWhiteSpace(request.InstancesRoot))
        {
            throw new ArgumentException("The instances root is required.", nameof(request));
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

    private static string ResolveProfileDirectory(string instancesRoot, string profileId)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(instancesRoot));
        var profileDirectory = ManifestValidator.ResolveManagedPath(root, profileId);
        if (!Directory.Exists(profileDirectory))
        {
            throw new DirectoryNotFoundException(profileDirectory);
        }

        EnsureDirectoryIsNotReparsePoint(profileDirectory);
        return profileDirectory;
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
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveExitedProcess(string profileId)
    {
        if (!_runningProcesses.TryGetValue(profileId, out var process))
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
        }

        RemoveAndDisposeProcess(profileId, process);
    }

    private void RemoveAndDisposeProcess(string profileId, Process process)
    {
        if (_runningProcesses.TryRemove(
                new KeyValuePair<string, Process>(profileId, process)))
        {
            process.Dispose();
        }
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
    RuntimePreparation,
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

public sealed class MinecraftLaunchSessionExpiredException
    : Exception
{
    public MinecraftLaunchSessionExpiredException()
        : base("The Minecraft launch session has expired.")
    {
    }
}
