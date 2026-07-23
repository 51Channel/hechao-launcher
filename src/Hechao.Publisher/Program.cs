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
                case "protect-oss-credential":
                    ProtectOssCredential(options);
                    return 0;
                case "upload-oss":
                    await UploadOssAsync(options);
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

        if (!Directory.Exists(sourceDirectory))
        {
            throw new PublisherUsageException($"Source directory does not exist: {sourceDirectory}");
        }

        if (!File.Exists(signingKeyInput.Path))
        {
            throw new PublisherUsageException($"Private key does not exist: {signingKeyInput.Path}");
        }

        if (IsWithin(sourceDirectory, outputDirectory) ||
            IsWithin(sourceDirectory, signingKeyInput.Path))
        {
            throw new PublisherUsageException(
                "Output directories and private keys must not be placed inside the client source directory.");
        }

        ManifestValidator.ValidateProfileId(profileId);
        var files = new List<ClientManifestFile>();
        long totalBytes = 0;
        foreach (var filePath in EnumerateSourceFiles(sourceDirectory)
                     .OrderBy(
                         path => Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/'),
                         StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            ManifestValidator.ValidateManagedPath(relativePath);
            var file = new FileInfo(filePath);
            var digest = await FileHashing.ComputeSha256Async(filePath);
            var objectRelativePath = $"objects/{digest[..2]}/{digest}";
            var objectPath = Path.Combine(outputDirectory, objectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            await CopyObjectAsync(filePath, objectPath, file.Length, digest);

            files.Add(new ClientManifestFile(
                relativePath,
                file.Length,
                digest,
                new Uri(objectBaseUri, objectRelativePath).AbsoluteUri,
                Required: true));
            totalBytes = checked(totalBytes + file.Length);
        }

        var deletePaths = options.All("delete").ToArray();
        var manifest = new ClientManifest(
            ManifestValidator.CurrentSchemaVersion,
            profileId,
            version,
            minecraftVersion,
            javaVersion,
            loader,
            loaderVersion,
            publishedAt.ToUniversalTime(),
            files,
            deletePaths);

        using var signingKey = signingKeyInput.Load();
        var envelope = SignedManifestCodec.Sign(manifest, keyId, signingKey);
        var envelopeBytes = ManifestJson.SerializeEnvelope(envelope);
        var manifestDirectory = Path.Combine(outputDirectory, "manifests");
        var manifestPath = Path.Combine(manifestDirectory, profileId + ".json");
        Directory.CreateDirectory(manifestDirectory);
        await WriteAtomicallyAsync(manifestPath, envelopeBytes);

        var envelopeDigest = Convert.ToHexString(SHA256.HashData(envelopeBytes)).ToLowerInvariant();
        Console.WriteLine($"Published profile: {profileId} {version}");
        Console.WriteLine($"Files: {files.Count}");
        Console.WriteLine($"Bytes: {totalBytes}");
        Console.WriteLine($"Manifest: {manifestPath}");
        Console.WriteLine($"Manifest SHA-256: {envelopeDigest}");
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

    private static IEnumerable<string> EnumerateSourceFiles(string sourceDirectory)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(sourceDirectory));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Symbolic links and reparse points are not allowed: {directory.FullName}");
            }

            foreach (var childDirectory in directory.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                pending.Push(childDirectory);
            }

            foreach (var file in directory.EnumerateFiles().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Symbolic links and reparse points are not allowed: {file.FullName}");
                }

                yield return file.FullName;
            }
        }
    }

    private static async Task CopyObjectAsync(
        string sourcePath,
        string objectPath,
        long expectedSize,
        string expectedSha256)
    {
        if (await FileHashing.MatchesAsync(objectPath, expectedSize, expectedSha256))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
        var temporaryPath = objectPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            if (!await FileHashing.MatchesAsync(temporaryPath, expectedSize, expectedSha256))
            {
                throw new ManifestIntegrityException($"Object verification failed after copying {sourcePath}.");
            }

            File.Move(temporaryPath, objectPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] content)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
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

    private static bool IsWithin(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
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
