using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hechao.Backup;

internal sealed record BackupEnvelopeHeader(
    int Version,
    string Algorithm,
    string KeyId,
    DateTimeOffset CreatedAt,
    long PlaintextLength,
    string PlaintextSha256,
    int ChunkSize,
    long ChunkCount,
    string BaseNonce,
    string WrappedKey);

internal static class BackupEnvelope
{
    internal const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int TagSize = 16;
    private const int MaximumHeaderBytes = 64 * 1024;
    private const string Algorithm = "RSA-OAEP-SHA256+A256GCM-CHUNKED";
    private static readonly byte[] Magic = "HCBAK001"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string GenerateKeyPair(
        string publicKeyPath,
        string privateKeyPath,
        string passphrasePath)
    {
        var passphrase = ReadPassphrase(passphrasePath);
        try
        {
            var publicPath = PrepareNewOutput(publicKeyPath);
            var privatePath = PrepareNewOutput(privateKeyPath);

            using var rsa = RSA.Create(4096);
            var publicBytes = rsa.ExportSubjectPublicKeyInfo();
            var privateBytes = rsa.ExportEncryptedPkcs8PrivateKey(
                passphrase,
                new PbeParameters(
                    PbeEncryptionAlgorithm.Aes256Cbc,
                    HashAlgorithmName.SHA256,
                    600_000));
            try
            {
                File.WriteAllText(
                    publicPath,
                    rsa.ExportSubjectPublicKeyInfoPem(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllBytes(privatePath, privateBytes);
                RestrictPrivateFile(privatePath);
                return Convert.ToHexString(SHA256.HashData(publicBytes));
            }
            catch
            {
                TryDelete(publicPath);
                TryDelete(privatePath);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
            }
        }
        finally
        {
            Array.Fill(passphrase, '\0');
        }
    }

    internal static BackupEnvelopeHeader Encrypt(
        string inputPath,
        string outputPath,
        string publicKeyPath,
        int chunkSize = DefaultChunkSize)
    {
        var input = RequireRegularInput(inputPath);
        return EncryptCore(
            input.OpenRead,
            input.Length,
            ComputeSha256(input.FullName),
            outputPath,
            publicKeyPath,
            chunkSize);
    }

    internal static BackupEnvelopeHeader EncryptBytes(
        ReadOnlySpan<byte> plaintext,
        string outputPath,
        string publicKeyPath,
        int chunkSize = DefaultChunkSize)
    {
        var copy = plaintext.ToArray();
        try
        {
            return EncryptCore(
                () => new MemoryStream(
                    copy,
                    index: 0,
                    count: copy.Length,
                    writable: false,
                    publiclyVisible: false),
                copy.Length,
                Convert.ToHexString(SHA256.HashData(copy)),
                outputPath,
                publicKeyPath,
                chunkSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    private static BackupEnvelopeHeader EncryptCore(
        Func<Stream> openSource,
        long plaintextLength,
        string plaintextSha256,
        string outputPath,
        string publicKeyPath,
        int chunkSize)
    {
        if (chunkSize is < 64 * 1024 or > 64 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkSize),
                "Chunk size must be between 64 KiB and 64 MiB.");
        }

        var output = PrepareNewOutput(outputPath);
        var temporaryOutput = CreateTemporarySibling(output);
        var publicPem = File.ReadAllText(publicKeyPath, Encoding.UTF8);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicPem);
        if (rsa.KeySize < 3072)
        {
            throw new CryptographicException(
                "The backup recovery public key must be at least 3072 bits.");
        }

        var publicBytes = rsa.ExportSubjectPublicKeyInfo();
        var keyId = Convert.ToHexString(SHA256.HashData(publicBytes));
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var baseNonce = RandomNumberGenerator.GetBytes(8);
        var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        var chunkCount = plaintextLength == 0
            ? 0
            : checked((plaintextLength + chunkSize - 1) / chunkSize);
        var header = new BackupEnvelopeHeader(
            1,
            Algorithm,
            keyId,
            DateTimeOffset.UtcNow,
            plaintextLength,
            plaintextSha256,
            chunkSize,
            chunkCount,
            Convert.ToBase64String(baseNonce),
            Convert.ToBase64String(wrappedKey));
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        if (headerBytes.Length > MaximumHeaderBytes)
        {
            throw new InvalidDataException("Backup envelope header is too large.");
        }

        try
        {
            {
                using var destination = new FileStream(
                    temporaryOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    FileOptions.SequentialScan);
                destination.Write(Magic);
                WriteUInt32(destination, checked((uint)headerBytes.Length));
                destination.Write(headerBytes);

                var headerHash = SHA256.HashData(headerBytes);
                var plaintext = new byte[chunkSize];
                var ciphertext = new byte[chunkSize];
                var tag = new byte[TagSize];
                using var aes = new AesGcm(aesKey, TagSize);
                using var source = openSource();
                for (uint index = 0; index < chunkCount; index++)
                {
                    var expectedLength = ExpectedChunkLength(
                        plaintextLength,
                        chunkSize,
                        index,
                        chunkCount);
                    ReadExactly(source, plaintext.AsSpan(0, expectedLength));
                    var nonce = CreateNonce(baseNonce, index);
                    var associatedData = CreateAssociatedData(
                        headerHash,
                        index,
                        expectedLength);
                    aes.Encrypt(
                        nonce,
                        plaintext.AsSpan(0, expectedLength),
                        ciphertext.AsSpan(0, expectedLength),
                        tag,
                        associatedData);
                    WriteUInt32(destination, checked((uint)expectedLength));
                    destination.Write(ciphertext, 0, expectedLength);
                    destination.Write(tag);
                }

                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryOutput, output);
            return header;
        }
        catch
        {
            TryDelete(temporaryOutput);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(wrappedKey);
        }
    }

    internal static BackupEnvelopeHeader Decrypt(
        string inputPath,
        string outputPath,
        string privateKeyPath,
        string passphrasePath)
    {
        var input = RequireRegularInput(inputPath);
        var output = PrepareNewOutput(outputPath);
        var temporaryOutput = CreateTemporarySibling(output);
        var passphrase = ReadPassphrase(passphrasePath);
        var privateBytes = File.ReadAllBytes(privateKeyPath);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportEncryptedPkcs8PrivateKey(
                passphrase,
                privateBytes,
                out var bytesRead);
            if (bytesRead != privateBytes.Length)
            {
                throw new CryptographicException(
                    "The encrypted recovery private key contains trailing data.");
            }

            using var source = input.OpenRead();
            var parsed = ReadHeader(source);
            var header = parsed.Header;
            var baseNonce = Convert.FromBase64String(header.BaseNonce);
            if (baseNonce.Length != 8)
            {
                throw new InvalidDataException(
                    "Backup envelope base nonce is invalid.");
            }

            var wrappedKey = Convert.FromBase64String(header.WrappedKey);
            var aesKey = rsa.Decrypt(
                wrappedKey,
                RSAEncryptionPadding.OaepSHA256);
            if (aesKey.Length != 32)
            {
                throw new CryptographicException(
                    "Backup envelope data key is invalid.");
            }

            try
            {
                using var destination = new FileStream(
                    temporaryOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    FileOptions.SequentialScan);
                var headerHash = SHA256.HashData(parsed.RawBytes);
                var ciphertext = new byte[header.ChunkSize];
                var plaintext = new byte[header.ChunkSize];
                var tag = new byte[TagSize];
                using var aes = new AesGcm(aesKey, TagSize);
                for (uint index = 0; index < header.ChunkCount; index++)
                {
                    var declaredLength = checked((int)ReadUInt32(source));
                    var expectedLength = ExpectedChunkLength(
                        header.PlaintextLength,
                        header.ChunkSize,
                        index,
                        header.ChunkCount);
                    if (declaredLength != expectedLength)
                    {
                        throw new InvalidDataException(
                            "Backup envelope chunk length is invalid.");
                    }

                    ReadExactly(source, ciphertext.AsSpan(0, declaredLength));
                    ReadExactly(source, tag);
                    var nonce = CreateNonce(baseNonce, index);
                    var associatedData = CreateAssociatedData(
                        headerHash,
                        index,
                        declaredLength);
                    aes.Decrypt(
                        nonce,
                        ciphertext.AsSpan(0, declaredLength),
                        tag,
                        plaintext.AsSpan(0, declaredLength),
                        associatedData);
                    destination.Write(plaintext, 0, declaredLength);
                }

                if (source.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        "Backup envelope contains trailing data.");
                }

                destination.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(aesKey);
                CryptographicOperations.ZeroMemory(wrappedKey);
            }

            var restored = new FileInfo(temporaryOutput);
            if (restored.Length != header.PlaintextLength ||
                !string.Equals(
                    ComputeSha256(restored.FullName),
                    header.PlaintextSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    "Restored backup length or SHA-256 does not match the envelope.");
            }

            File.Move(temporaryOutput, output);
            return header;
        }
        catch
        {
            TryDelete(temporaryOutput);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
            Array.Fill(passphrase, '\0');
        }
    }

    internal static BackupEnvelopeHeader Inspect(string inputPath)
    {
        using var input = RequireRegularInput(inputPath).OpenRead();
        return ReadHeader(input).Header;
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static ParsedHeader ReadHeader(Stream source)
    {
        Span<byte> magic = stackalloc byte[Magic.Length];
        ReadExactly(source, magic);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The file is not a Hechao encrypted backup envelope.");
        }

        var headerLength = checked((int)ReadUInt32(source));
        if (headerLength is <= 0 or > MaximumHeaderBytes)
        {
            throw new InvalidDataException(
                "Backup envelope header length is invalid.");
        }

        var headerBytes = new byte[headerLength];
        ReadExactly(source, headerBytes);
        var header = JsonSerializer.Deserialize<BackupEnvelopeHeader>(
            headerBytes,
            JsonOptions)
            ?? throw new InvalidDataException(
                "Backup envelope header is empty.");
        ValidateHeader(header);
        return new ParsedHeader(header, headerBytes);
    }

    private static void ValidateHeader(BackupEnvelopeHeader header)
    {
        if (header.Version != 1 ||
            !string.Equals(header.Algorithm, Algorithm, StringComparison.Ordinal) ||
            header.KeyId.Length != 64 ||
            !header.KeyId.All(Uri.IsHexDigit) ||
            header.PlaintextLength < 0 ||
            header.PlaintextSha256.Length != 64 ||
            !header.PlaintextSha256.All(Uri.IsHexDigit) ||
            header.ChunkSize is < 64 * 1024 or > 64 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Backup envelope header fields are invalid.");
        }

        var expectedChunkCount = header.PlaintextLength == 0
            ? 0
            : checked(
                (header.PlaintextLength + header.ChunkSize - 1) /
                header.ChunkSize);
        if (header.ChunkCount != expectedChunkCount ||
            header.ChunkCount > uint.MaxValue)
        {
            throw new InvalidDataException(
                "Backup envelope chunk count is invalid.");
        }
    }

    private static int ExpectedChunkLength(
        long totalLength,
        int chunkSize,
        uint index,
        long chunkCount)
    {
        if (index >= chunkCount)
        {
            throw new InvalidDataException(
                "Backup envelope chunk index is invalid.");
        }

        var offset = checked((long)index * chunkSize);
        return checked((int)Math.Min(chunkSize, totalLength - offset));
    }

    private static byte[] CreateNonce(byte[] baseNonce, uint index)
    {
        var nonce = new byte[12];
        baseNonce.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
        return nonce;
    }

    private static byte[] CreateAssociatedData(
        byte[] headerHash,
        uint index,
        int length)
    {
        var associatedData = new byte[40];
        headerHash.CopyTo(associatedData, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            associatedData.AsSpan(32),
            index);
        BinaryPrimitives.WriteUInt32BigEndian(
            associatedData.AsSpan(36),
            checked((uint)length));
        return associatedData;
    }

    private static char[] ReadPassphrase(string path)
    {
        const int maximumPassphraseBytes = 16 * 1024;
        var file = RequireRegularInput(path);
        if (file.Length is <= 0 or > maximumPassphraseBytes)
        {
            throw new ArgumentException(
                "The recovery passphrase file is invalid.");
        }

        var encoded = File.ReadAllBytes(file.FullName);
        char[]? decoded = null;
        try
        {
            decoded = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetChars(encoded);
            var length = decoded.Length;
            while (length > 0 && decoded[length - 1] is '\r' or '\n')
            {
                length--;
            }

            if (length < 32)
            {
                throw new ArgumentException(
                    "The recovery passphrase must contain at least 32 characters.");
            }

            var passphrase = new char[length];
            decoded.AsSpan(0, length).CopyTo(passphrase);
            return passphrase;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            if (decoded is not null)
            {
                Array.Fill(decoded, '\0');
            }
        }
    }

    private static FileInfo RequireRegularInput(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileNotFoundException(
                "A regular input file is required.",
                file.FullName);
        }

        return file;
    }

    private static string PrepareNewOutput(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException($"Output already exists: {fullPath}");
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new IOException("Output path has no parent directory.");
        }

        Directory.CreateDirectory(parent);
        return fullPath;
    }

    private static string CreateTemporarySibling(string outputPath) =>
        $"{outputPath}.tmp-{Guid.NewGuid():N}";

    private static void RestrictPrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void WriteUInt32(Stream destination, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        destination.Write(bytes);
    }

    private static uint ReadUInt32(Stream source)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadExactly(source, bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static void ReadExactly(Stream source, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            var read = source.Read(destination);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Backup envelope ended unexpectedly.");
            }

            destination = destination[read..];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Preserve the original failure.
        }
    }

    private sealed record ParsedHeader(
        BackupEnvelopeHeader Header,
        byte[] RawBytes);
}
