using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record OssCredential(
    string AccessKeyId,
    string AccessKeySecret);

internal sealed record OssCredentialMetadata(
    int SchemaVersion,
    string Provider,
    string RamUser,
    string Policy,
    string Bucket,
    string ObjectPrefix,
    string Protection,
    string EntropyLabel,
    DateTimeOffset CreatedAtUtc,
    string CipherSha256);

internal static class OssCredentialStore
{
    private const int MaximumCredentialBytes = 32 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void Protect(
        OssCredential credential,
        string outputPath,
        string metadataPath,
        string entropyLabel,
        OssCredentialMetadata metadata)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PublisherUsageException("DPAPI credential protection requires Windows.");
        }

        ValidateCredential(credential);
        ValidateEntropyLabel(entropyLabel);
        outputPath = Path.GetFullPath(outputPath);
        metadataPath = Path.GetFullPath(metadataPath);
        if (string.Equals(outputPath, metadataPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new PublisherUsageException(
                "Credential ciphertext and metadata must use different files.");
        }

        EnsureNewFile(outputPath);
        EnsureNewFile(metadataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credential, SerializerOptions);
        byte[]? ciphertext = null;
        try
        {
            ciphertext = ProtectedData.Protect(
                plaintext,
                Encoding.UTF8.GetBytes(entropyLabel),
                DataProtectionScope.CurrentUser);
            using (var output = new FileStream(
                       outputPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                output.Write(ciphertext);
                output.Flush(flushToDisk: true);
            }

            var completedMetadata = metadata with
            {
                SchemaVersion = 1,
                Protection = "Windows DPAPI CurrentUser",
                EntropyLabel = entropyLabel,
                CipherSha256 = Convert.ToHexString(SHA256.HashData(ciphertext))
            };
            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(
                completedMetadata,
                SerializerOptions);
            try
            {
                using var metadataOutput = new FileStream(
                    metadataPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough);
                metadataOutput.Write(metadataBytes);
                metadataOutput.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadataBytes);
            }
        }
        catch
        {
            File.Delete(outputPath);
            File.Delete(metadataPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
    }

    public static OssCredential Load(
        string encryptedPath,
        string entropyLabel)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PublisherUsageException("DPAPI credentials can only be decrypted on Windows.");
        }

        ValidateEntropyLabel(entropyLabel);
        encryptedPath = Path.GetFullPath(encryptedPath);
        var file = new FileInfo(encryptedPath);
        if (!file.Exists ||
            file.Length is <= 0 or > MaximumCredentialBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException("The encrypted OSS credential file is invalid.");
        }

        var ciphertext = File.ReadAllBytes(encryptedPath);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(
                ciphertext,
                Encoding.UTF8.GetBytes(entropyLabel),
                DataProtectionScope.CurrentUser);
            var credential = JsonSerializer.Deserialize<OssCredential>(
                plaintext,
                SerializerOptions) ?? throw new PublisherUsageException(
                "The OSS credential payload is empty.");
            ValidateCredential(credential);
            return credential;
        }
        catch (JsonException exception)
        {
            throw new PublisherUsageException(
                $"The OSS credential payload is invalid: {exception.Message}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static void ValidateCredential(OssCredential credential)
    {
        if (!IsAsciiAlphaNumeric(credential.AccessKeyId, 8, 128) ||
            !IsAsciiAlphaNumeric(credential.AccessKeySecret, 16, 128))
        {
            throw new PublisherUsageException("The OSS credential values are invalid.");
        }
    }

    private static void ValidateEntropyLabel(string entropyLabel)
    {
        if (string.IsNullOrWhiteSpace(entropyLabel) || entropyLabel.Length > 512)
        {
            throw new PublisherUsageException("The DPAPI entropy label is invalid.");
        }
    }

    private static bool IsAsciiAlphaNumeric(string value, int minimum, int maximum)
    {
        return value.Length >= minimum &&
               value.Length <= maximum &&
               value.All(character =>
                   character is >= 'A' and <= 'Z' or
                       >= 'a' and <= 'z' or
                       >= '0' and <= '9');
    }

    private static void EnsureNewFile(string path)
    {
        if (File.Exists(path))
        {
            throw new PublisherUsageException($"Refusing to overwrite an existing file: {path}");
        }
    }
}
