using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hechao.ServerControlAgent;

public sealed record ServerControlAgentConfiguration
{
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string TokenPath { get; init; } = string.Empty;
    public string StateDirectory { get; init; } =
        @"C:\ProgramData\Hechao\ServerControlAgent";
    public string ConsoleSubmitScript { get; init; } =
        @"C:\ProgramData\Hechao\ServerControl\Submit-MinecraftConsoleCommand.ps1";
    public int PollSeconds { get; init; } = 2;
    public int HeartbeatSeconds { get; init; } = 5;
    public IReadOnlyList<ServerControlTargetConfiguration> Targets { get; init; } =
        [];

    public static ServerControlAgentConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The server control agent configuration does not exist.",
                fullPath);
        }

        var text = File.ReadAllText(fullPath);
        var configuration = JsonSerializer.Deserialize<
            ServerControlAgentConfiguration>(
            text,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException(
                "The server control agent configuration is empty.");
        var normalized = configuration with
        {
            TokenPath = Path.GetFullPath(configuration.TokenPath),
            StateDirectory = Path.GetFullPath(configuration.StateDirectory),
            ConsoleSubmitScript =
                Path.GetFullPath(configuration.ConsoleSubmitScript),
            Targets = [.. configuration.Targets.Select(target => target.Normalize())]
        };
        normalized.Validate();
        return normalized;
    }

    public void Validate()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var baseUri) ||
            (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
             !(string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
               baseUri.IsLoopback)) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidDataException(
                "ApiBaseUrl must be an HTTPS origin or loopback HTTP origin.");
        }

        if (!ConfigurationPatterns.AgentId().IsMatch(AgentId) ||
            !Path.IsPathFullyQualified(TokenPath) ||
            !Path.IsPathFullyQualified(StateDirectory) ||
            !Path.IsPathFullyQualified(ConsoleSubmitScript) ||
            PollSeconds is < 1 or > 30 ||
            HeartbeatSeconds is < 2 or > 60 ||
            Targets.Count is < 1 or > 32 ||
            Targets.Select(target => target.ServerId)
                .Distinct(StringComparer.Ordinal)
                .Count() != Targets.Count)
        {
            throw new InvalidDataException(
                "The server control agent configuration is invalid.");
        }

        foreach (var target in Targets)
        {
            target.Validate();
        }

        var deletionTargets = Targets
            .Where(target => target.ServerDeletionEnabled)
            .ToArray();
        foreach (var target in deletionTargets)
        {
            if (ContainsPath(target.ServerDirectory, TokenPath) ||
                ContainsPath(target.ServerDirectory, StateDirectory) ||
                ContainsPath(target.ServerDirectory, ConsoleSubmitScript) ||
                Targets.Any(other =>
                    !ReferenceEquals(target, other) &&
                    (ContainsPath(
                         target.ServerDirectory,
                         other.ServerDirectory) ||
                     ContainsPath(
                         other.ServerDirectory,
                         target.ServerDirectory))))
            {
                throw new InvalidDataException(
                    $"Deletion target '{target.ServerId}' overlaps protected agent data or another managed server.");
            }
        }

        foreach (var samePort in Targets.GroupBy(target => target.Port))
        {
            if (samePort.Count() <= 1)
            {
                continue;
            }

            var groups = samePort
                .Select(target => target.ConflictGroup)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (groups.Length != 1 || groups[0] is null)
            {
                throw new InvalidDataException(
                    $"Targets sharing port {samePort.Key} must share one conflict group.");
            }
        }
    }

    private static bool ContainsPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        return string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ServerControlTargetConfiguration
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerDirectory { get; init; } = string.Empty;
    public string StartTaskName { get; init; } = string.Empty;
    public int Port { get; init; }
    public string? ConflictGroup { get; init; }
    public string LogRelativePath { get; init; } = @"logs\latest.log";
    public string PropertiesRelativePath { get; init; } = "server.properties";
    public string MemorySettingsRelativePath { get; init; } = "start.bat";
    public string StartScriptRelativePath { get; init; } = "start.bat";
    public int MaximumAllowedMemoryMiB { get; init; } = 65536;
    public bool PackageDeploymentEnabled { get; init; }
    public bool ServerDeletionEnabled { get; init; }
    public IReadOnlyList<string> HostManagedRelativePaths { get; init; } = [];
    public IReadOnlyList<string> WorldDataRelativePaths { get; init; } = [];
    public IReadOnlyList<string> AllowedCommandPrefixes { get; init; } =
        ["list", "say", "whitelist", "save-all"];

    internal ServerControlTargetConfiguration Normalize() =>
        this with
        {
            ServerDirectory = Path.GetFullPath(ServerDirectory),
            LogRelativePath = NormalizeRelativePath(LogRelativePath),
            PropertiesRelativePath = NormalizeRelativePath(
                PropertiesRelativePath),
            MemorySettingsRelativePath = NormalizeRelativePath(
                MemorySettingsRelativePath),
            StartScriptRelativePath = NormalizeRelativePath(
                StartScriptRelativePath),
            HostManagedRelativePaths = NormalizeRelativePaths(
                HostManagedRelativePaths),
            WorldDataRelativePaths = NormalizeRelativePaths(
                WorldDataRelativePaths),
            AllowedCommandPrefixes = [.. AllowedCommandPrefixes
                .Select(prefix => prefix.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]
        };

    internal void Validate()
    {
        var hostManagedPaths = HostManagedRelativePaths ?? [];
        var worldDataPaths = WorldDataRelativePaths ?? [];
        var deploymentPathsAreSafe =
            HostManagedRelativePaths is not null &&
            WorldDataRelativePaths is not null &&
            hostManagedPaths.All(IsSafeRelativePath) &&
            worldDataPaths.All(IsSafeRelativePath);
        var protectedDeploymentPathsAreSafe =
            IsSafeRelativePath(PropertiesRelativePath) &&
            IsSafeRelativePath(MemorySettingsRelativePath) &&
            IsSafeRelativePath(StartScriptRelativePath);
        var deploymentPathsConflict = deploymentPathsAreSafe &&
            protectedDeploymentPathsAreSafe &&
            HasDeploymentPathConflict(hostManagedPaths, worldDataPaths);
        if (!ConfigurationPatterns.ServerId().IsMatch(ServerId) ||
            !Path.IsPathFullyQualified(ServerDirectory) ||
            !ConfigurationPatterns.TaskName().IsMatch(StartTaskName) ||
            Port is < 1 or > 65535 ||
            (ConflictGroup is not null &&
             !ConfigurationPatterns.ConflictGroup().IsMatch(ConflictGroup)) ||
            !IsSafeRelativePath(LogRelativePath) ||
            !IsSafeRelativePath(PropertiesRelativePath) ||
            !IsSafeRelativePath(MemorySettingsRelativePath) ||
            !IsSafeRelativePath(StartScriptRelativePath) ||
            string.Equals(
                PropertiesRelativePath,
                MemorySettingsRelativePath,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                PropertiesRelativePath,
                StartScriptRelativePath,
                StringComparison.OrdinalIgnoreCase) ||
            MaximumAllowedMemoryMiB is < 512 or > 65536 ||
            MaximumAllowedMemoryMiB % 256 != 0 ||
            !deploymentPathsAreSafe ||
            hostManagedPaths.Count > 32 ||
            worldDataPaths.Count > 32 ||
            deploymentPathsConflict ||
            (!PackageDeploymentEnabled &&
             (hostManagedPaths.Count > 0 ||
              worldDataPaths.Count > 0)) ||
            (ServerDeletionEnabled && !IsSafeDeletionRoot()) ||
            AllowedCommandPrefixes.Count is < 1 or > 64 ||
            AllowedCommandPrefixes.Any(prefix =>
                !ConfigurationPatterns.CommandPrefix().IsMatch(prefix)))
        {
            throw new InvalidDataException(
                $"Server control target '{ServerId}' is invalid.");
        }
    }

    internal string GetLogPath() =>
        GetContainedPath(LogRelativePath);

    internal string GetPropertiesPath() =>
        GetContainedPath(PropertiesRelativePath);

    internal string GetMemorySettingsPath() =>
        GetContainedPath(MemorySettingsRelativePath);

    internal string GetStartScriptPath() =>
        GetContainedPath(StartScriptRelativePath);

    internal string GetContainedDeploymentPath(string relativePath) =>
        GetContainedPath(relativePath);

    private bool IsSafeDeletionRoot()
    {
        var path = Path.GetFullPath(ServerDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var volumeRoot = Path.GetPathRoot(path)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(volumeRoot) &&
               !string.Equals(path, volumeRoot, StringComparison.OrdinalIgnoreCase) &&
               Directory.GetParent(path) is not null;
    }

    private string GetContainedPath(string relativePath)
    {
        var root = Path.GetFullPath(ServerDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A configured server path escapes its server directory.");
        }

        return path;
    }

    private static bool IsSafeRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        !Path.IsPathFullyQualified(value) &&
        !value.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private static IReadOnlyList<string> NormalizeRelativePaths(
        IReadOnlyList<string> paths) =>
        [.. paths
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    private bool HasDeploymentPathConflict(
        IReadOnlyList<string> hostManagedPaths,
        IReadOnlyList<string> worldDataPaths)
    {
        var hostManaged = NormalizeRelativePaths(hostManagedPaths);
        var worldData = NormalizeRelativePaths(worldDataPaths);
        var preserved = hostManaged.Concat(worldData).ToArray();
        var protectedPaths = new[]
        {
            NormalizeRelativePath(PropertiesRelativePath),
            NormalizeRelativePath(MemorySettingsRelativePath),
            NormalizeRelativePath(StartScriptRelativePath),
            ServerPackageDeployer.DeploymentMarkerName
        };
        return hostManaged.Count != hostManagedPaths.Count ||
               worldData.Count != worldDataPaths.Count ||
               HasOverlappingPaths(preserved) ||
               preserved.Any(path => protectedPaths.Any(
                   protectedPath => PathsOverlap(path, protectedPath)));
    }

    private static bool HasOverlappingPaths(IReadOnlyList<string> paths)
    {
        for (var left = 0; left < paths.Count; left++)
        {
            for (var right = left + 1; right < paths.Count; right++)
            {
                if (PathsOverlap(paths[left], paths[right]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool PathsOverlap(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
        left.StartsWith(
            right + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ||
        right.StartsWith(
            left + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelativePath(string path) =>
        path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
}

internal static partial class ConfigurationPatterns
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex AgentId();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex ServerId();

    [GeneratedRegex("^Hechao-Server-[A-Za-z0-9._-]{1,64}$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex TaskName();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex ConflictGroup();

    [GeneratedRegex("^[a-z0-9][a-z0-9:_-]{0,63}$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex CommandPrefix();
}
