using System.Security.Cryptography;
using System.Text;

namespace Hechao.Publisher.Tests;

public sealed class SigningKeyInputTests
{
    [Fact]
    public void Load_DecryptsCurrentUserDpapiKeyAndChecksDigest()
    {
        const string entropyLabel = "Hechao.Publisher.Tests/SigningKey/v1";
        using var fixture = DpapiKeyFixture.Create(entropyLabel);
        var options = CommandOptions.Parse(
        [
            "--private-key-dpapi", fixture.EncryptedPath,
            "--dpapi-entropy-label", entropyLabel,
            "--dpapi-blob-sha256", fixture.EncryptedSha256
        ]);

        var input = SigningKeyInput.Parse(options);
        using var loadedKey = input.Load();

        Assert.Equal(
            fixture.PublicKey,
            loadedKey.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Load_RejectsWrongEncryptedBlobDigest()
    {
        const string entropyLabel = "Hechao.Publisher.Tests/SigningKey/v1";
        using var fixture = DpapiKeyFixture.Create(entropyLabel);
        var options = CommandOptions.Parse(
        [
            "--private-key-dpapi", fixture.EncryptedPath,
            "--dpapi-entropy-label", entropyLabel,
            "--dpapi-blob-sha256", new string('0', 64)
        ]);

        var input = SigningKeyInput.Parse(options);

        Assert.Throws<PublisherUsageException>(() => input.Load());
    }

    [Fact]
    public void Parse_RequiresExactlyOnePrivateKeySource()
    {
        var missing = CommandOptions.Parse([]);
        var multiple = CommandOptions.Parse(
        [
            "--private-key", "plain.pem",
            "--private-key-dpapi", "encrypted.dpapi",
            "--dpapi-entropy-label", "label"
        ]);

        Assert.Throws<PublisherUsageException>(() => SigningKeyInput.Parse(missing));
        Assert.Throws<PublisherUsageException>(() => SigningKeyInput.Parse(multiple));
    }

    private sealed class DpapiKeyFixture(
        string directory,
        string encryptedPath,
        string encryptedSha256,
        byte[] publicKey) : IDisposable
    {
        public string EncryptedPath { get; } = encryptedPath;
        public string EncryptedSha256 { get; } = encryptedSha256;
        public byte[] PublicKey { get; } = publicKey;

        public static DpapiKeyFixture Create(string entropyLabel)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var encryptedPath = Path.Combine(directory, "private.dpapi");

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privateKey = key.ExportPkcs8PrivateKey();
            var pemCharacters = PemEncoding.Write("PRIVATE KEY", privateKey);
            var pemBytes = Encoding.UTF8.GetBytes(pemCharacters);
            byte[]? encrypted = null;
            try
            {
                encrypted = ProtectedData.Protect(
                    pemBytes,
                    Encoding.UTF8.GetBytes(entropyLabel),
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(encryptedPath, encrypted);
                return new DpapiKeyFixture(
                    directory,
                    encryptedPath,
                    Convert.ToHexString(SHA256.HashData(encrypted)),
                    key.ExportSubjectPublicKeyInfo());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
                Array.Fill(pemCharacters, '\0');
                CryptographicOperations.ZeroMemory(pemBytes);
                if (encrypted is not null)
                {
                    CryptographicOperations.ZeroMemory(encrypted);
                }
            }
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(PublicKey);
            Directory.Delete(directory, recursive: true);
        }
    }
}
