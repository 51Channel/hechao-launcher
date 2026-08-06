using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed partial record PackagePublisherAgentConfiguration
{
    internal const string WindowsDpapiSecretStorage = "windows-dpapi";
    internal const string SystemdCredentialSecretStorage = "systemd-credentials";

    public string ApiBaseUrl { get; init; } = string.Empty;
    public string PublicObjectBaseUrl { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string SecretStorage { get; init; } = WindowsDpapiSecretStorage;
    public string TokenPath { get; init; } = string.Empty;
    public string StateDirectory { get; init; } = string.Empty;
    public int PollSeconds { get; init; } = 3;
    public string SigningKeyId { get; init; } = string.Empty;
    public string SigningKeyPath { get; init; } = string.Empty;
    public string? SigningKeyEntropyLabel { get; init; }
    public string? SigningKeyBlobSha256 { get; init; }
    public string OssBucket { get; init; } = string.Empty;
    public string OssRegion { get; init; } = string.Empty;
    public string OssEndpoint { get; init; } = string.Empty;
    public string OssObjectPrefix { get; init; } = "objects";
    public string OssCredentialPath { get; init; } = string.Empty;
    public string? OssCredentialEntropyLabel { get; init; }
    public int Parallelism { get; init; } = 8;
    public long MinimumFreeBytes { get; init; } = 1024L * 1024 * 1024;
    public int WorkingSpaceExpansionMultiplier { get; init; } = 4;

    internal bool UsesWindowsDpapi =>
        string.Equals(
            SecretStorage,
            WindowsDpapiSecretStorage,
            StringComparison.Ordinal);

    internal bool UsesSystemdCredentials =>
        string.Equals(
            SecretStorage,
            SystemdCredentialSecretStorage,
            StringComparison.Ordinal);

    internal static PackagePublisherAgentConfiguration Load(string path) =>
        Load(path, Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY"));

    internal static PackagePublisherAgentConfiguration Load(
        string path,
        string? credentialsDirectory)
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
        var secretStorage = (configuration.SecretStorage ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        configuration = configuration with
        {
            SecretStorage = secretStorage,
            TokenPath = ResolveSecretPath(
                configuration.TokenPath,
                secretStorage,
                credentialsDirectory),
            StateDirectory = Path.GetFullPath(configuration.StateDirectory),
            SigningKeyPath = ResolveSecretPath(
                configuration.SigningKeyPath,
                secretStorage,
                credentialsDirectory),
            OssCredentialPath = ResolveSecretPath(
                configuration.OssCredentialPath,
                secretStorage,
                credentialsDirectory),
            OssObjectPrefix = configuration.OssObjectPrefix.Trim('/')
        };
        configuration.Validate();
        return configuration;
    }

    internal void Validate()
    {
        var validSecretStorage =
            (UsesWindowsDpapi &&
             SigningKeyEntropyLabel?.Length is >= 8 and <= 512 &&
             OssCredentialEntropyLabel?.Length is >= 8 and <= 512) ||
            (UsesSystemdCredentials &&
             string.IsNullOrEmpty(SigningKeyEntropyLabel) &&
             string.IsNullOrEmpty(OssCredentialEntropyLabel) &&
             SigningKeyBlobSha256 is null);
        if (!TryGetApiBaseUri(out _) ||
            !TryGetPublicObjectBaseUri(out _) ||
            !AgentIdPattern().IsMatch(AgentId) ||
            !validSecretStorage ||
            !Path.IsPathFullyQualified(TokenPath) ||
            !Path.IsPathFullyQualified(StateDirectory) ||
            !Path.IsPathFullyQualified(SigningKeyPath) ||
            !Path.IsPathFullyQualified(OssCredentialPath) ||
            PollSeconds is < 1 or > 30 ||
            !SigningKeyIdPattern().IsMatch(SigningKeyId) ||
            (SigningKeyBlobSha256 is not null &&
             !Sha256Pattern().IsMatch(SigningKeyBlobSha256)) ||
            !BucketPattern().IsMatch(OssBucket) ||
            !RegionPattern().IsMatch(OssRegion) ||
            !TryGetOssEndpoint(out _) ||
            !ObjectPrefixPattern().IsMatch(OssObjectPrefix) ||
            Parallelism is < 1 or > 32 ||
            MinimumFreeBytes is < 512L * 1024 * 1024 or > 100L * 1024 * 1024 * 1024 ||
            WorkingSpaceExpansionMultiplier is < 2 or > 250)
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
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (protectedPaths.Distinct(pathComparer).Count() !=
            protectedPaths.Length)
        {
            throw new InvalidDataException(
                "The package publisher protected files must use distinct paths.");
        }
    }

    internal void ValidateRuntimePlatform()
    {
        if (UsesWindowsDpapi && !OperatingSystem.IsWindows())
        {
            throw new PublisherUsageException(
                "Windows DPAPI package publisher credentials require Windows.");
        }

        if (UsesSystemdCredentials && OperatingSystem.IsWindows())
        {
            throw new PublisherUsageException(
                "systemd package publisher credentials require Linux.");
        }
    }

    internal bool TryGetApiBaseUri(out Uri uri) =>
        TryGetServiceOrigin(ApiBaseUrl, allowLoopbackHttp: true, out uri);

    internal bool TryGetPublicObjectBaseUri(out Uri uri) =>
        TryGetServiceOrigin(PublicObjectBaseUrl, allowLoopbackHttp: false, out uri);

    internal bool TryGetOssEndpoint(out Uri uri) =>
        TryGetServiceOrigin(OssEndpoint, allowLoopbackHttp: false, out uri);

    internal Uri GetProfileObjectBaseUri(string profileId)
    {
        if (!TryGetPublicObjectBaseUri(out var origin))
        {
            throw new InvalidDataException(
                "The package publisher public object base URL is invalid.");
        }

        return new Uri(
            origin,
            $"/v1/profiles/{Uri.EscapeDataString(profileId)}/");
    }

    private static bool TryGetServiceOrigin(
        string value,
        bool allowLoopbackHttp,
        out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             allowLoopbackHttp &&
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

    private static string ResolveSecretPath(
        string value,
        string secretStorage,
        string? credentialsDirectory)
    {
        if (!string.Equals(
                secretStorage,
                SystemdCredentialSecretStorage,
                StringComparison.Ordinal))
        {
            return Path.GetFullPath(value);
        }

        if (!CredentialNamePattern().IsMatch(value) ||
            string.IsNullOrWhiteSpace(credentialsDirectory) ||
            !Path.IsPathFullyQualified(credentialsDirectory))
        {
            throw new InvalidDataException(
                "The systemd credential directory or credential name is invalid.");
        }

        var root = Path.GetFullPath(credentialsDirectory);
        var resolved = Path.GetFullPath(Path.Combine(root, value));
        if (!string.Equals(
                Path.GetDirectoryName(resolved),
                root.TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The systemd credential path escaped its credential directory.");
        }

        return resolved;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialNamePattern();

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
