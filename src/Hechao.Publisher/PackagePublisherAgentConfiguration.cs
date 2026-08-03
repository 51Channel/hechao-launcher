using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed partial record PackagePublisherAgentConfiguration
{
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string TokenPath { get; init; } = string.Empty;
    public string StateDirectory { get; init; } = string.Empty;
    public int PollSeconds { get; init; } = 3;
    public string SigningKeyId { get; init; } = string.Empty;
    public string SigningKeyPath { get; init; } = string.Empty;
    public string SigningKeyEntropyLabel { get; init; } = string.Empty;
    public string? SigningKeyBlobSha256 { get; init; }
    public string OssBucket { get; init; } = string.Empty;
    public string OssRegion { get; init; } = string.Empty;
    public string OssEndpoint { get; init; } = string.Empty;
    public string OssObjectPrefix { get; init; } = "objects";
    public string OssCredentialPath { get; init; } = string.Empty;
    public string OssCredentialEntropyLabel { get; init; } = string.Empty;
    public int Parallelism { get; init; } = 8;

    internal static PackagePublisherAgentConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length is <= 0 or > 1024 * 1024 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The package publisher agent configuration is missing or invalid.");
        }

        var configuration = JsonSerializer.Deserialize<
            PackagePublisherAgentConfiguration>(
            File.ReadAllText(fullPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException(
                "The package publisher agent configuration is empty.");
        configuration = configuration with
        {
            TokenPath = Path.GetFullPath(configuration.TokenPath),
            StateDirectory = Path.GetFullPath(configuration.StateDirectory),
            SigningKeyPath = Path.GetFullPath(configuration.SigningKeyPath),
            OssCredentialPath = Path.GetFullPath(configuration.OssCredentialPath),
            OssObjectPrefix = configuration.OssObjectPrefix.Trim('/')
        };
        configuration.Validate();
        return configuration;
    }

    internal void Validate()
    {
        if (!TryGetApiBaseUri(out _) ||
            !AgentIdPattern().IsMatch(AgentId) ||
            !Path.IsPathFullyQualified(TokenPath) ||
            !Path.IsPathFullyQualified(StateDirectory) ||
            !Path.IsPathFullyQualified(SigningKeyPath) ||
            !Path.IsPathFullyQualified(OssCredentialPath) ||
            PollSeconds is < 1 or > 30 ||
            !SigningKeyIdPattern().IsMatch(SigningKeyId) ||
            SigningKeyEntropyLabel.Length is < 8 or > 512 ||
            (SigningKeyBlobSha256 is not null &&
             !Sha256Pattern().IsMatch(SigningKeyBlobSha256)) ||
            !BucketPattern().IsMatch(OssBucket) ||
            !RegionPattern().IsMatch(OssRegion) ||
            !TryGetOssEndpoint(out _) ||
            !ObjectPrefixPattern().IsMatch(OssObjectPrefix) ||
            OssCredentialEntropyLabel.Length is < 8 or > 512 ||
            Parallelism is < 1 or > 32)
        {
            throw new InvalidDataException(
                "The package publisher agent configuration is invalid.");
        }

        var protectedPaths = new[]
        {
            TokenPath,
            SigningKeyPath,
            OssCredentialPath
        };
        if (protectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            protectedPaths.Length)
        {
            throw new InvalidDataException(
                "The package publisher protected files must use distinct paths.");
        }
    }

    internal bool TryGetApiBaseUri(out Uri uri) =>
        TryGetHttpsOrigin(ApiBaseUrl, out uri);

    internal bool TryGetOssEndpoint(out Uri uri) =>
        TryGetHttpsOrigin(OssEndpoint, out uri);

    private static bool TryGetHttpsOrigin(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.AbsolutePath is "" or "/"))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{2,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex SigningKeyIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex BucketPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9/_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectPrefixPattern();
}
