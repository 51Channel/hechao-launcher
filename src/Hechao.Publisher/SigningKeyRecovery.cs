using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hechao.Backup;
using Hechao.Distribution;

internal sealed record SigningKeyRecoveryExportResult(
    string SigningKeyId,
    string SigningPublicKeySha256,
    string RecoveryKeyId,
    string EnvelopeSha256);

internal sealed record SigningKeyRecoveryMetadata(
    int SchemaVersion,
    string KeyId,
    string Algorithm,
    DateTimeOffset RecoveredAtUtc,
    string Protection,
    string EntropyLabel,
    string PublicKeySha256,
    string EncryptedBlobSha256);

internal static class SigningKeyRecovery
{
    private const int MaximumPrivateKeyBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static SigningKeyRecoveryExportResult Export(
        ECDsa signingKey,
        string keyId,
        string trustBundlePath,
        string recoveryPublicKeyPath,
        string outputPath)
    {
        var publicKeySha256 = ValidateTrustedKey(
            signingKey,
            keyId,
            trustBundlePath);
        var privateKeyBytes = signingKey.ExportPkcs8PrivateKey();
        try
        {
            var header = BackupEnvelope.EncryptBytes(
                privateKeyBytes,
                outputPath,
                recoveryPublicKeyPath);
            return new SigningKeyRecoveryExportResult(
                keyId,
                publicKeySha256,
                header.KeyId,
                BackupEnvelope.ComputeSha256(outputPath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    internal static SigningKeyRecoveryMetadata RestoreToDpapi(
        string recoveredPrivateKeyPath,
        string keyId,
        string trustBundlePath,
        string outputDpapiPath,
        string metadataOutputPath,
        string entropyLabel)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PublisherUsageException(
                "Signing key DPAPI restoration requires Windows.");
        }

        if (string.IsNullOrWhiteSpace(entropyLabel) ||
            entropyLabel.Length > 512)
        {
            throw new PublisherUsageException(
                "The DPAPI entropy label is invalid.");
        }

        EnsureNewPath(outputDpapiPath);
        EnsureNewPath(metadataOutputPath);
        var privateKeyBytes = ReadPrivateKey(recoveredPrivateKeyPath);
        byte[]? plaintextPem = null;
        byte[]? ciphertext = null;
        try
        {
            using var signingKey = ECDsa.Create();
            signingKey.ImportPkcs8PrivateKey(
                privateKeyBytes,
                out var bytesRead);
            if (bytesRead != privateKeyBytes.Length)
            {
                throw new PublisherUsageException(
                    "The recovered signing key contains trailing data.");
            }

            var publicKeySha256 = ValidateTrustedKey(
                signingKey,
                keyId,
                trustBundlePath);
            plaintextPem = Utf8NoBom.GetBytes(
                signingKey.ExportPkcs8PrivateKeyPem());
            ciphertext = ProtectedData.Protect(
                plaintextPem,
                Encoding.UTF8.GetBytes(entropyLabel),
                DataProtectionScope.CurrentUser);
            var metadata = new SigningKeyRecoveryMetadata(
                1,
                keyId,
                "ECDSA_P256_SHA256",
                DateTimeOffset.UtcNow,
                "Windows DPAPI CurrentUser",
                entropyLabel,
                publicKeySha256,
                Convert.ToHexString(SHA256.HashData(ciphertext)));

            WriteNewFile(outputDpapiPath, ciphertext);
            try
            {
                WriteNewFile(
                    metadataOutputPath,
                    JsonSerializer.SerializeToUtf8Bytes(
                        metadata,
                        JsonOptions));
            }
            catch
            {
                File.Delete(outputDpapiPath);
                throw;
            }

            return metadata;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
            if (plaintextPem is not null)
            {
                CryptographicOperations.ZeroMemory(plaintextPem);
            }
            if (ciphertext is not null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
    }

    private static string ValidateTrustedKey(
        ECDsa signingKey,
        string keyId,
        string trustBundlePath)
    {
        if (signingKey.KeySize != 256)
        {
            throw new PublisherUsageException(
                "The signing key must use NIST P-256.");
        }

        var bundle = ManifestJson.DeserializeTrustBundle(
            File.ReadAllBytes(trustBundlePath));
        var trustedKey = bundle.Keys.SingleOrDefault(key =>
            string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
            ?? throw new PublisherUsageException(
                $"Signing key {keyId} is not present in the trust bundle.");
        var actualKey = SignedManifestCodec.ExportTrustKey(
            keyId,
            signingKey);
        if (!string.Equals(
                actualKey.Algorithm,
                trustedKey.Algorithm,
                StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(actualKey.PublicKeyBase64),
                Convert.FromBase64String(trustedKey.PublicKeyBase64)))
        {
            throw new PublisherUsageException(
                "The recovered signing key does not match the trust bundle.");
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Convert.FromBase64String(actualKey.PublicKeyBase64)));
    }

    private static byte[] ReadPrivateKey(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists ||
            file.Length is <= 0 or > MaximumPrivateKeyBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException(
                "The recovered signing key file is invalid.");
        }

        return File.ReadAllBytes(file.FullName);
    }

    private static void EnsureNewPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new PublisherUsageException(
                $"Recovery output already exists: {fullPath}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new PublisherUsageException(
                "Recovery output path has no parent directory."));
    }

    private static void WriteNewFile(string path, ReadOnlySpan<byte> content)
    {
        var fullPath = Path.GetFullPath(path);
        using var output = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        output.Write(content);
        output.Flush(flushToDisk: true);
    }
}
