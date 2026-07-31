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
        configuration.Validate();
        return configuration with
        {
            TokenPath = Path.GetFullPath(configuration.TokenPath),
            StateDirectory = Path.GetFullPath(configuration.StateDirectory),
            ConsoleSubmitScript =
                Path.GetFullPath(configuration.ConsoleSubmitScript),
            Targets = [.. configuration.Targets.Select(target => target.Normalize())]
        };
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
    public int MaximumAllowedMemoryMiB { get; init; } = 65536;
    public IReadOnlyList<string> AllowedCommandPrefixes { get; init; } =
        ["list", "say", "whitelist", "save-all"];

    internal ServerControlTargetConfiguration Normalize() =>
        this with
        {
            ServerDirectory = Path.GetFullPath(ServerDirectory),
            AllowedCommandPrefixes = [.. AllowedCommandPrefixes
                .Select(prefix => prefix.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]
        };

    internal void Validate()
    {
        if (!ConfigurationPatterns.ServerId().IsMatch(ServerId) ||
            !Path.IsPathFullyQualified(ServerDirectory) ||
            !ConfigurationPatterns.TaskName().IsMatch(StartTaskName) ||
            Port is < 1 or > 65535 ||
            (ConflictGroup is not null &&
             !ConfigurationPatterns.ConflictGroup().IsMatch(ConflictGroup)) ||
            !IsSafeRelativePath(LogRelativePath) ||
            !IsSafeRelativePath(PropertiesRelativePath) ||
            !IsSafeRelativePath(MemorySettingsRelativePath) ||
            string.Equals(
                PropertiesRelativePath,
                MemorySettingsRelativePath,
                StringComparison.OrdinalIgnoreCase) ||
            MaximumAllowedMemoryMiB is < 512 or > 65536 ||
            MaximumAllowedMemoryMiB % 256 != 0 ||
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
        !Path.IsPathFullyQualified(value) &&
        !value.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
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
