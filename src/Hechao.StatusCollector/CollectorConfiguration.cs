using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hechao.StatusCollector;

public sealed class CollectorConfiguration
{
    private static readonly Regex NamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex TargetPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    public string ApiEndpoint { get; init; } = string.Empty;

    public string CollectorInstance { get; init; } = string.Empty;

    public string TokenPath { get; init; } = string.Empty;

    public int ProbeTimeoutSeconds { get; init; } = 3;

    public int MetricsMaxAgeSeconds { get; init; } = 30;

    public IReadOnlyList<ServerProbeConfiguration> Servers { get; init; } = [];

    public static CollectorConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        var configuration = JsonSerializer.Deserialize<CollectorConfiguration>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidDataException("The collector configuration is empty.");

        var baseDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The collector configuration path is invalid.");
        if (string.IsNullOrWhiteSpace(configuration.TokenPath))
        {
            throw new InvalidDataException("TokenPath is required.");
        }

        var tokenPath = Path.IsPathRooted(configuration.TokenPath)
            ? configuration.TokenPath
            : Path.Combine(baseDirectory, configuration.TokenPath);
        configuration = new CollectorConfiguration
        {
            ApiEndpoint = configuration.ApiEndpoint,
            CollectorInstance = configuration.CollectorInstance,
            TokenPath = Path.GetFullPath(tokenPath),
            ProbeTimeoutSeconds = configuration.ProbeTimeoutSeconds,
            MetricsMaxAgeSeconds = configuration.MetricsMaxAgeSeconds,
            Servers = (configuration.Servers ?? [])
                .Select(server => NormalizeServerPaths(server, baseDirectory))
                .ToArray()
        };
        configuration.Validate();
        return configuration;
    }

    private void Validate()
    {
        if (!Uri.TryCreate(ApiEndpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidDataException(
                "ApiEndpoint must be an HTTPS URL, except for loopback testing.");
        }

        if (!NamePattern.IsMatch(CollectorInstance))
        {
            throw new InvalidDataException("CollectorInstance is invalid.");
        }

        if (ProbeTimeoutSeconds is < 1 or > 15)
        {
            throw new InvalidDataException("ProbeTimeoutSeconds must be between 1 and 15.");
        }

        if (MetricsMaxAgeSeconds is < 10 or > 300)
        {
            throw new InvalidDataException(
                "MetricsMaxAgeSeconds must be between 10 and 300.");
        }

        if (Servers.Count is < 1 or > 64)
        {
            throw new InvalidDataException("Servers must contain between 1 and 64 targets.");
        }

        if (Servers.Select(server => server.VelocityTarget)
            .Distinct(StringComparer.Ordinal)
            .Count() != Servers.Count)
        {
            throw new InvalidDataException("Servers contains duplicate Velocity targets.");
        }

        foreach (var server in Servers)
        {
            if (!TargetPattern.IsMatch(server.VelocityTarget) ||
                string.IsNullOrWhiteSpace(server.Host) ||
                server.Host.Length > 253 ||
                server.Host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
                server.Port is < 1 or > 65535 ||
                server.FallbackMaxPlayers is < 0 or > 10000 ||
                HasInvalidPath(server.DataPath) ||
                HasInvalidPath(server.MetricsPath))
            {
                throw new InvalidDataException(
                    $"Server probe configuration is invalid for '{server.VelocityTarget}'.");
            }
        }
    }

    private static ServerProbeConfiguration NormalizeServerPaths(
        ServerProbeConfiguration server,
        string baseDirectory)
    {
        var dataPath = NormalizeOptionalPath(server.DataPath, baseDirectory);
        var metricsPath = NormalizeOptionalPath(
            server.MetricsPath,
            baseDirectory);
        if (metricsPath is null && dataPath is not null)
        {
            metricsPath = Path.Combine(
                dataPath,
                "plugins",
                "HechaoServerMetrics",
                "metrics.json");
        }

        return new ServerProbeConfiguration
        {
            VelocityTarget = server.VelocityTarget,
            Host = server.Host,
            Port = server.Port,
            FallbackMaxPlayers = server.FallbackMaxPlayers,
            DataPath = dataPath,
            MetricsPath = metricsPath,
            AllowStaleMetricsWhenEmpty = server.AllowStaleMetricsWhenEmpty
        };
    }

    private static string? NormalizeOptionalPath(
        string? path,
        string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidate = path.Trim();
        return Path.GetFullPath(
            Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(baseDirectory, candidate));
    }

    private static bool HasInvalidPath(string? path) =>
        path is not null &&
        (path.Length > 1024 ||
         !Path.IsPathFullyQualified(path) ||
         path.Any(char.IsControl));
}

public sealed class ServerProbeConfiguration
{
    public string VelocityTarget { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public int FallbackMaxPlayers { get; init; }

    public string? DataPath { get; init; }

    public string? MetricsPath { get; init; }

    public bool AllowStaleMetricsWhenEmpty { get; init; }
}
