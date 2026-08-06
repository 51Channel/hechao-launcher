using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hechao.Distribution;

return await PublisherProgram.RunAsync(args);

internal static class PublisherProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            var options = CommandOptions.Parse(args.Skip(1).ToArray());
            switch (args[0].ToLowerInvariant())
            {
                case "keygen":
                    GenerateKey(options);
                    return 0;
                case "publish":
                    await PublishAsync(options);
                    return 0;
                case "verify":
                    Verify(options);
                    return 0;
                case "validate-release":
                    ValidateRelease(options);
                    return 0;
                case "protect-oss-credential":
                    ProtectOssCredential(options);
                    return 0;
                case "upload-oss":
                    await UploadOssAsync(options);
                    return 0;
                case "upload-launcher-release":
                    await UploadLauncherReleaseAsync(options);
                    return 0;
                case "export-signing-recovery":
                    ExportSigningRecovery(options);
                    return 0;
                case "restore-signing-recovery":
                    RestoreSigningRecovery(options);
                    return 0;
                case "protect-package-agent-token":
                    ProtectPackageAgentToken(options);
                    return 0;
                case "validate-package-agent":
                    ValidatePackageAgent(options);
                    return 0;
                case "run-package-agent":
                    await RunPackageAgentAsync(options);
                    return 0;
                default:
                    throw new PublisherUsageException($"Unknown command: {args[0]}");
            }
        }
        catch (PublisherUsageException exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or ManifestFormatException)
        {
            Console.Error.WriteLine($"Publish failed: {exception.Message}");
            return 1;
        }
    }

    private static void GenerateKey(CommandOptions options)
    {
        var keyId = options.Required("key-id");
        var privateKeyPath = Path.GetFullPath(options.Required("private-key"));
        var trustBundlePath = Path.GetFullPath(options.Required("trust-bundle"));
        if (string.Equals(privateKeyPath, trustBundlePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new PublisherUsageException("Private key and trust bundle must use different paths.");
        }

        EnsureNewFile(privateKeyPath);
        EnsureNewFile(trustBundlePath);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trustKey = SignedManifestCodec.ExportTrustKey(keyId, key);
        var bundle = new ManifestTrustBundle(1, [trustKey]);

        Directory.CreateDirectory(Path.GetDirectoryName(privateKeyPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(trustBundlePath)!);
        File.WriteAllText(privateKeyPath, key.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));
        File.WriteAllBytes(trustBundlePath, ManifestJson.SerializeTrustBundle(bundle));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                privateKeyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Console.WriteLine($"Created signing key: {keyId}");
        Console.WriteLine($"Private key: {privateKeyPath}");
        Console.WriteLine($"Public trust bundle: {trustBundlePath}");
        Console.WriteLine("Keep the private key offline. Only the public trust bundle belongs in the launcher.");
    }

    private static async Task PublishAsync(CommandOptions options)
    {
        var sourceDirectory = Path.GetFullPath(options.Required("source"));
        var outputDirectory = Path.GetFullPath(options.Required("output"));
        var profileId = options.Required("profile-id");
        var version = options.Required("version");
        var minecraftVersion = options.Required("minecraft-version");
        var javaVersion = options.Required("java-version");
        var loader = options.Required("loader");
        var loaderVersion = options.Required("loader-version");
        var keyId = options.Required("key-id");
        var signingKeyInput = SigningKeyInput.Parse(options);
        var objectBaseUri = ParseObjectBaseUri(options.Required("object-base-url"));
        var publishedAt = options.Optional("published-at") is { } publishedAtValue
            ? DateTimeOffset.Parse(publishedAtValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UtcNow;

        var deletePaths = options.All("delete").ToArray();
        var result = await ClientDistributionBuilder.BuildAsync(
            new ClientDistributionBuildOptions(
            sourceDirectory,
            outputDirectory,
            profileId,
            version,
            minecraftVersion,
            javaVersion,
            loader,
            loaderVersion,
            keyId,
            signingKeyInput,
            objectBaseUri,
            publishedAt,
            deletePaths));
        Console.WriteLine($"Published profile: {profileId} {version}");
        Console.WriteLine($"Files: {result.FileCount}");
        Console.WriteLine($"Bytes: {result.TotalBytes}");
        Console.WriteLine($"Manifest: {result.ManifestPath}");
        Console.WriteLine($"Manifest SHA-256: {result.ManifestSha256}");
    }

    private static void Verify(CommandOptions options)
    {
        var manifestPath = Path.GetFullPath(options.Required("manifest"));
        var trustBundlePath = Path.GetFullPath(options.Required("trust-bundle"));
        var trustBundle = ManifestJson.DeserializeTrustBundle(File.ReadAllBytes(trustBundlePath));
        var verified = SignedManifestCodec.Verify(File.ReadAllBytes(manifestPath), trustBundle);

        Console.WriteLine($"Verified profile: {verified.Manifest.ProfileId} {verified.Manifest.Version}");
        Console.WriteLine($"Signing key: {verified.KeyId}");
        Console.WriteLine($"Files: {verified.Manifest.Files.Count}");
        Console.WriteLine($"Manifest SHA-256: {verified.EnvelopeSha256}");
    }

    private static void ValidateRelease(CommandOptions options)
    {
        var result = DistributionReleaseValidator.Validate(
            options.Required("distribution"),
            options.Required("manifest"),
            options.Required("trust-bundle"));

        Console.WriteLine($"Validated release: {result.ProfileId} {result.Version}");
        Console.WriteLine($"Published at: {result.PublishedAt:O}");
        Console.WriteLine($"Signing key: {result.KeyId}");
        Console.WriteLine($"Manifest SHA-256: {result.ManifestSha256}");
        Console.WriteLine(
            $"Logical files: {result.LogicalFileCount} bytes={result.LogicalBytes}");
        Console.WriteLine($"Objects: {result.ObjectCount} bytes={result.ObjectBytes}");
    }

    private static void ProtectOssCredential(CommandOptions options)
    {
        if (!Console.IsInputRedirected)
        {
            throw new PublisherUsageException(
                "OSS AccessKey ID and secret must be provided as two redirected input lines.");
        }

        var accessKeyId = Console.ReadLine();
        var accessKeySecret = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new PublisherUsageException("The OSS credential input is incomplete.");
        }

        var outputPath = Path.GetFullPath(options.Required("output"));
        var metadataPath = Path.GetFullPath(options.Required("metadata-output"));
        var entropyLabel = options.Required("dpapi-entropy-label");
        var metadata = new OssCredentialMetadata(
            SchemaVersion: 1,
            Provider: "Alibaba Cloud RAM",
            RamUser: options.Required("ram-user"),
            Policy: options.Required("policy"),
            Bucket: options.Required("bucket"),
            ObjectPrefix: options.Required("object-prefix"),
            Protection: "Windows DPAPI CurrentUser",
            EntropyLabel: entropyLabel,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CipherSha256: string.Empty);
        OssCredentialStore.Protect(
            new OssCredential(accessKeyId, accessKeySecret),
            outputPath,
            metadataPath,
            entropyLabel,
            metadata);
        Console.WriteLine($"Protected OSS credential: {outputPath}");
        Console.WriteLine($"Credential metadata: {metadataPath}");
    }

    private static async Task UploadOssAsync(CommandOptions options)
    {
        var distributionDirectory = Path.GetFullPath(options.Required("distribution"));
        var credentialPath = Path.GetFullPath(options.Required("credential-dpapi"));
        var entropyLabel = options.Required("dpapi-entropy-label");
        var parallelismValue = options.Optional("parallelism") ?? "8";
        if (!int.TryParse(parallelismValue, out var parallelism) ||
            parallelism is < 1 or > 32)
        {
            throw new PublisherUsageException("--parallelism must be between 1 and 32.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        var uploader = new OssDistributionUploader(
            new OssUploadOptions(
                distributionDirectory,
                options.Required("bucket"),
                options.Required("region"),
                options.Required("endpoint"),
                options.Required("object-prefix"),
                credentialPath,
                entropyLabel,
                parallelism));
        var result = await uploader.UploadAsync(cancellation.Token);
        Console.WriteLine($"Uploaded objects: {result.Uploaded}");
        Console.WriteLine($"Already present: {result.AlreadyPresent}");
        Console.WriteLine($"Uploaded bytes: {result.UploadedBytes}");
    }

    private static async Task UploadLauncherReleaseAsync(CommandOptions options)
    {
        var linkMinutesValue = options.Optional("link-minutes") ?? "60";
        if (!int.TryParse(linkMinutesValue, out var linkMinutes) ||
            linkMinutes is < 5 or > 1440)
        {
            throw new PublisherUsageException(
                "--link-minutes must be between 5 and 1440.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        var uploader = new LauncherReleaseUploader(
            new LauncherReleaseUploadOptions(
                Path.GetFullPath(options.Required("installer")),
                options.Required("version"),
                options.Required("sha256"),
                options.Required("bucket"),
                options.Required("region"),
                options.Required("endpoint"),
                options.Required("download-endpoint"),
                Path.GetFullPath(options.Required("credential-dpapi")),
                options.Required("dpapi-entropy-label"),
                TimeSpan.FromMinutes(linkMinutes)));
        var result = await uploader.UploadAsync(cancellation.Token);
        Console.WriteLine(
            result.Uploaded
                ? "Uploaded launcher release."
                : "Launcher release already present and verified.");
        Console.WriteLine($"Object: {result.ObjectKey}");
        Console.WriteLine($"Bytes: {result.Length}");
        Console.WriteLine($"SHA-256: {result.Sha256}");
        Console.WriteLine(
            $"Internal download link expires: {result.DownloadUrlExpiresAt:O}");
        Console.WriteLine(result.DownloadUrl);
    }

    private static void ExportSigningRecovery(CommandOptions options)
    {
        var keyId = options.Required("key-id");
        var signingKeyInput = SigningKeyInput.Parse(options);
        using var signingKey = signingKeyInput.Load();
        var result = SigningKeyRecovery.Export(
            signingKey,
            keyId,
            Path.GetFullPath(options.Required("trust-bundle")),
            Path.GetFullPath(options.Required("recovery-public-key")),
            Path.GetFullPath(options.Required("output")));

        Console.WriteLine($"Exported signing recovery envelope: {result.SigningKeyId}");
        Console.WriteLine($"Signing public key SHA-256: {result.SigningPublicKeySha256}");
        Console.WriteLine($"Recovery key ID: {result.RecoveryKeyId}");
        Console.WriteLine($"Envelope SHA-256: {result.EnvelopeSha256}");
    }

    private static void RestoreSigningRecovery(CommandOptions options)
    {
        var metadata = SigningKeyRecovery.RestoreToDpapi(
            Path.GetFullPath(options.Required("recovered-private-key")),
            options.Required("key-id"),
            Path.GetFullPath(options.Required("trust-bundle")),
            Path.GetFullPath(options.Required("output-dpapi")),
            Path.GetFullPath(options.Required("metadata-output")),
            options.Required("dpapi-entropy-label"));

        Console.WriteLine($"Restored signing key: {metadata.KeyId}");
        Console.WriteLine($"Signing public key SHA-256: {metadata.PublicKeySha256}");
        Console.WriteLine($"Encrypted blob SHA-256: {metadata.EncryptedBlobSha256}");
    }

    private static void ProtectPackageAgentToken(CommandOptions options)
    {
        if (!Console.IsInputRedirected)
        {
            throw new PublisherUsageException(
                "The package publisher token must be provided as one redirected input line.");
        }

        var token = Console.ReadLine() ?? string.Empty;
        PackagePublisherProtectedTokenStore.Protect(
            token,
            options.Required("output"));
        Console.WriteLine("Protected package publisher agent token.");
    }

    private static async Task RunPackageAgentAsync(CommandOptions options)
    {
        var configuration = PackagePublisherAgentConfiguration.Load(
            options.Required("config"));
        configuration.ValidateRuntimePlatform();
        Directory.CreateDirectory(configuration.StateDirectory);
        var token = configuration.UsesWindowsDpapi
            ? PackagePublisherProtectedTokenStore.Read(configuration.TokenPath)
            : PackagePublisherSystemdCredentialStore.ReadToken(configuration.TokenPath);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate |
                System.Net.DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = false
        };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(
                configuration.ApiBaseUrl.TrimEnd('/') + "/",
                UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            PublisherProductInfo.UserAgent);
        var worker = new PackagePublisherWorker(
            configuration,
            new PackagePublisherApiClient(
                httpClient,
                configuration.AgentId,
                token));
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellation.Cancel();
        await worker.RunAsync(cancellation.Token);
    }

    private static void ValidatePackageAgent(CommandOptions options)
    {
        var configuration = PackagePublisherAgentConfiguration.Load(
            options.Required("config"));
        configuration.ValidateRuntimePlatform();
        var token = configuration.UsesWindowsDpapi
            ? PackagePublisherProtectedTokenStore.Read(configuration.TokenPath)
            : PackagePublisherSystemdCredentialStore.ReadToken(configuration.TokenPath);
        if (!PackagePublisherProtectedTokenStore.IsValidToken(token))
        {
            throw new PublisherUsageException(
                "The package publisher token is invalid.");
        }

        using var signingKey = new SigningKeyInput(
            configuration.SigningKeyPath,
            configuration.SigningKeyEntropyLabel,
            configuration.SigningKeyBlobSha256?.ToUpperInvariant()).Load();
        _ = OssCredentialStore.Load(
            configuration.OssCredentialPath,
            configuration.OssCredentialEntropyLabel);
        Console.WriteLine("Package publisher agent configuration is valid.");
    }

    private static Uri ParseObjectBaseUri(string value)
    {
        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)))
        {
            throw new PublisherUsageException("Object base URL must use HTTPS, except for loopback development URLs.");
        }

        return uri;
    }

    private static void EnsureNewFile(string path)
    {
        if (File.Exists(path))
        {
            throw new PublisherUsageException($"Refusing to overwrite an existing key file: {path}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"{PublisherProductInfo.ProductName} {PublisherProductInfo.Version}");
        Console.WriteLine();
        Console.WriteLine("Generate an offline signing key:");
        Console.WriteLine("  keygen --key-id <id> --private-key <path> --trust-bundle <path>");
        Console.WriteLine();
        Console.WriteLine("Publish a client directory:");
        Console.WriteLine("  publish --source <dir> --output <dir> --profile-id <id> --version <version>");
        Console.WriteLine("          --minecraft-version <version> --java-version <version>");
        Console.WriteLine("          --loader <name> --loader-version <version>");
        Console.WriteLine("          --object-base-url <https-url> --key-id <id>");
        Console.WriteLine("          (--private-key <path> | --private-key-dpapi <path>");
        Console.WriteLine("           --dpapi-entropy-label <label> [--dpapi-blob-sha256 <sha256>])");
        Console.WriteLine("          [--published-at <ISO-8601>] [--delete <relative-path>]...");
        Console.WriteLine();
        Console.WriteLine("Verify a signed manifest:");
        Console.WriteLine("  verify --manifest <path> --trust-bundle <path>");
        Console.WriteLine();
        Console.WriteLine("Validate a signed manifest and every immutable distribution object:");
        Console.WriteLine("  validate-release --distribution <dir> --manifest <path>");
        Console.WriteLine("          --trust-bundle <path>");
        Console.WriteLine();
        Console.WriteLine("Protect a publisher-only OSS credential from two redirected input lines:");
        Console.WriteLine("  protect-oss-credential --output <path> --metadata-output <path>");
        Console.WriteLine("          --dpapi-entropy-label <label> --ram-user <name> --policy <name>");
        Console.WriteLine("          --bucket <name> --object-prefix <prefix>");
        Console.WriteLine();
        Console.WriteLine("Upload immutable distribution objects to OSS:");
        Console.WriteLine("  upload-oss --distribution <dir> --bucket <name> --region <region>");
        Console.WriteLine("          --endpoint <https-url> --object-prefix <prefix>");
        Console.WriteLine("          --credential-dpapi <path> --dpapi-entropy-label <label>");
        Console.WriteLine("          [--parallelism <1-32>]");
        Console.WriteLine();
        Console.WriteLine("Upload one immutable launcher installer and create a private download link:");
        Console.WriteLine("  upload-launcher-release --installer <exe> --version <major.minor.patch>");
        Console.WriteLine("          --sha256 <digest> --bucket <name> --region <region>");
        Console.WriteLine("          --endpoint <https-oss-origin>");
        Console.WriteLine("          --download-endpoint <https-custom-domain>");
        Console.WriteLine("          --credential-dpapi <path> --dpapi-entropy-label <label>");
        Console.WriteLine("          [--link-minutes <5-1440>]");
        Console.WriteLine();
        Console.WriteLine("Export a signing key into an encrypted recovery envelope:");
        Console.WriteLine("  export-signing-recovery --key-id <id> --trust-bundle <path>");
        Console.WriteLine("          --recovery-public-key <pem> --output <hcbackup>");
        Console.WriteLine("          (--private-key <path> | --private-key-dpapi <path>");
        Console.WriteLine("           --dpapi-entropy-label <label> [--dpapi-blob-sha256 <sha256>])");
        Console.WriteLine();
        Console.WriteLine("Restore a decrypted signing key into a new CurrentUser DPAPI blob:");
        Console.WriteLine("  restore-signing-recovery --recovered-private-key <pkcs8>");
        Console.WriteLine("          --key-id <id> --trust-bundle <path>");
        Console.WriteLine("          --output-dpapi <path> --metadata-output <path>");
        Console.WriteLine("          --dpapi-entropy-label <label>");
        Console.WriteLine();
        Console.WriteLine("Protect the package publisher API token from one redirected input line:");
        Console.WriteLine("  protect-package-agent-token --output <path>");
        Console.WriteLine();
        Console.WriteLine("Validate package publisher credentials without network access:");
        Console.WriteLine("  validate-package-agent --config <absolute-json-path>");
        Console.WriteLine();
        Console.WriteLine("Run the resumable package publisher agent:");
        Console.WriteLine("  run-package-agent --config <absolute-json-path>");
    }
}

internal sealed record SigningKeyInput(
    string Path,
    string? DpapiEntropyLabel,
    string? DpapiBlobSha256)
{
    private const int MaximumPrivateKeyBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public bool IsDpapi => DpapiEntropyLabel is not null;

    public static SigningKeyInput Parse(CommandOptions options)
    {
        var plaintextPath = options.Optional("private-key");
        var dpapiPath = options.Optional("private-key-dpapi");
        if (string.IsNullOrWhiteSpace(plaintextPath) == string.IsNullOrWhiteSpace(dpapiPath))
        {
            throw new PublisherUsageException(
                "Specify exactly one of --private-key or --private-key-dpapi.");
        }

        if (!string.IsNullOrWhiteSpace(plaintextPath))
        {
            if (options.Optional("dpapi-entropy-label") is not null ||
                options.Optional("dpapi-blob-sha256") is not null)
            {
                throw new PublisherUsageException(
                    "DPAPI options may only be used with --private-key-dpapi.");
            }

            return new SigningKeyInput(
                System.IO.Path.GetFullPath(plaintextPath),
                DpapiEntropyLabel: null,
                DpapiBlobSha256: null);
        }

        var entropyLabel = options.Required("dpapi-entropy-label");
        if (string.IsNullOrWhiteSpace(entropyLabel) || entropyLabel.Length > 512)
        {
            throw new PublisherUsageException("The DPAPI entropy label is invalid.");
        }

        var expectedDigest = options.Optional("dpapi-blob-sha256");
        if (expectedDigest is not null &&
            (expectedDigest.Length != 64 || !expectedDigest.All(Uri.IsHexDigit)))
        {
            throw new PublisherUsageException("--dpapi-blob-sha256 must be a SHA-256 hex digest.");
        }

        return new SigningKeyInput(
            System.IO.Path.GetFullPath(dpapiPath!),
            entropyLabel,
            expectedDigest?.ToUpperInvariant());
    }

    public ECDsa Load()
    {
        if (!File.Exists(Path))
        {
            throw new PublisherUsageException($"Private key does not exist: {Path}");
        }

        var keyFile = new FileInfo(Path);
        if (keyFile.Length is <= 0 or > MaximumPrivateKeyBytes ||
            (keyFile.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException("The private key file is invalid.");
        }

        var sourceBytes = File.ReadAllBytes(Path);
        byte[]? privateKeyBytes = null;
        char[]? privateKeyCharacters = null;
        try
        {
            if (DpapiBlobSha256 is not null)
            {
                var actualDigest = Convert.ToHexString(SHA256.HashData(sourceBytes));
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(actualDigest),
                        Encoding.ASCII.GetBytes(DpapiBlobSha256)))
                {
                    throw new PublisherUsageException("The encrypted private key digest does not match.");
                }
            }

            if (IsDpapi)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PublisherUsageException(
                        "DPAPI private keys can only be decrypted on Windows.");
                }

                privateKeyBytes = ProtectedData.Unprotect(
                    sourceBytes,
                    Encoding.UTF8.GetBytes(DpapiEntropyLabel!),
                    DataProtectionScope.CurrentUser);
            }
            else
            {
                privateKeyBytes = sourceBytes;
            }

            privateKeyCharacters = new char[StrictUtf8.GetCharCount(privateKeyBytes)];
            StrictUtf8.GetChars(privateKeyBytes, privateKeyCharacters);
            var key = ECDsa.Create();
            try
            {
                key.ImportFromPem(privateKeyCharacters);
                return key;
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }
        finally
        {
            if (privateKeyCharacters is not null)
            {
                Array.Fill(privateKeyCharacters, '\0');
            }

            if (privateKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }

            if (!ReferenceEquals(sourceBytes, privateKeyBytes))
            {
                CryptographicOperations.ZeroMemory(sourceBytes);
            }
        }
    }
}

internal sealed class CommandOptions(Dictionary<string, List<string>> values)
{
    public static CommandOptions Parse(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new PublisherUsageException($"Expected --name value near: {key}");
            }

            key = key[2..];
            if (!values.TryGetValue(key, out var entries))
            {
                entries = [];
                values.Add(key, entries);
            }

            entries.Add(args[index + 1]);
        }

        return new CommandOptions(values);
    }

    public string Required(string name) =>
        Optional(name) ?? throw new PublisherUsageException($"Missing required option: --{name}");

    public string? Optional(string name) =>
        values.TryGetValue(name, out var entries) ? entries[^1] : null;

    public IReadOnlyList<string> All(string name) =>
        values.TryGetValue(name, out var entries) ? entries : [];
}

internal sealed class PublisherUsageException(string message) : Exception(message);
