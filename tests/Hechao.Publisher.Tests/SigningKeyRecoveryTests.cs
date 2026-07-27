using System.Security.Cryptography;
using System.Text;
using Hechao.Backup;
using Hechao.Distribution;

namespace Hechao.Publisher.Tests;

public sealed class SigningKeyRecoveryTests
{
    [Fact]
    public void ExportDecryptRestore_RoundTripsTrustedSigningKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RecoveryFixture.Create();
        var export = SigningKeyRecovery.Export(
            fixture.SigningKey,
            fixture.KeyId,
            fixture.TrustBundlePath,
            fixture.RecoveryPublicKeyPath,
            fixture.EnvelopePath);

        var header = BackupEnvelope.Decrypt(
            fixture.EnvelopePath,
            fixture.RecoveredPkcs8Path,
            fixture.RecoveryPrivateKeyPath,
            fixture.RecoveryPassphrasePath);
        var restored = SigningKeyRecovery.RestoreToDpapi(
            fixture.RecoveredPkcs8Path,
            fixture.KeyId,
            fixture.TrustBundlePath,
            fixture.RestoredDpapiPath,
            fixture.RestoredMetadataPath,
            fixture.EntropyLabel);
        var restoredInput = SigningKeyInput.Parse(
            CommandOptions.Parse(
            [
                "--private-key-dpapi", fixture.RestoredDpapiPath,
                "--dpapi-entropy-label", fixture.EntropyLabel,
                "--dpapi-blob-sha256", restored.EncryptedBlobSha256
            ]));
        using var restoredKey = restoredInput.Load();

        Assert.Equal(fixture.KeyId, export.SigningKeyId);
        Assert.Equal(export.RecoveryKeyId, header.KeyId);
        Assert.Equal(
            fixture.SigningKey.ExportSubjectPublicKeyInfo(),
            restoredKey.ExportSubjectPublicKeyInfo());
        Assert.Equal(export.SigningPublicKeySha256, restored.PublicKeySha256);
        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(
                    fixture.SigningKey.ExportSubjectPublicKeyInfo())),
            restored.PublicKeySha256);
        Assert.DoesNotContain(
            "PRIVATE KEY",
            Encoding.ASCII.GetString(
                File.ReadAllBytes(fixture.EnvelopePath)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Export_RejectsSigningKeyThatDoesNotMatchTrustBundle()
    {
        using var fixture = RecoveryFixture.Create();
        using var differentKey = ECDsa.Create(
            ECCurve.NamedCurves.nistP256);

        var exception = Assert.Throws<PublisherUsageException>(
            () => SigningKeyRecovery.Export(
                differentKey,
                fixture.KeyId,
                fixture.TrustBundlePath,
                fixture.RecoveryPublicKeyPath,
                fixture.EnvelopePath));

        Assert.Contains(
            "does not match",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.EnvelopePath));
    }

    [Fact]
    public void Restore_RejectsRecoveredKeyThatDoesNotMatchTrustBundle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RecoveryFixture.Create();
        using var differentKey = ECDsa.Create(
            ECCurve.NamedCurves.nistP256);
        var differentPkcs8 = differentKey.ExportPkcs8PrivateKey();
        try
        {
            File.WriteAllBytes(
                fixture.RecoveredPkcs8Path,
                differentPkcs8);
            var exception = Assert.Throws<PublisherUsageException>(
                () => SigningKeyRecovery.RestoreToDpapi(
                    fixture.RecoveredPkcs8Path,
                    fixture.KeyId,
                    fixture.TrustBundlePath,
                    fixture.RestoredDpapiPath,
                    fixture.RestoredMetadataPath,
                    fixture.EntropyLabel));

            Assert.Contains(
                "does not match",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.RestoredDpapiPath));
            Assert.False(File.Exists(fixture.RestoredMetadataPath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(differentPkcs8);
        }
    }

    private sealed class RecoveryFixture : IDisposable
    {
        private readonly string directory;

        private RecoveryFixture(
            string directory,
            ECDsa signingKey,
            string keyId)
        {
            this.directory = directory;
            SigningKey = signingKey;
            KeyId = keyId;
        }

        internal ECDsa SigningKey { get; }
        internal string KeyId { get; }
        internal string EntropyLabel { get; } =
            "Hechao.Publisher.Tests/SigningRecovery/v1";
        internal string TrustBundlePath =>
            Path.Combine(directory, "trust.json");
        internal string RecoveryPublicKeyPath =>
            Path.Combine(directory, "recovery-public.pem");
        internal string RecoveryPrivateKeyPath =>
            Path.Combine(directory, "recovery-private.p8");
        internal string RecoveryPassphrasePath =>
            Path.Combine(directory, "recovery-passphrase.txt");
        internal string EnvelopePath =>
            Path.Combine(directory, "signing-key.hcbackup");
        internal string RecoveredPkcs8Path =>
            Path.Combine(directory, "recovered-signing-key.pkcs8");
        internal string RestoredDpapiPath =>
            Path.Combine(directory, "restored-signing-key.dpapi");
        internal string RestoredMetadataPath =>
            Path.Combine(directory, "restored-signing-key.meta.json");

        internal static RecoveryFixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var signingKey = ECDsa.Create(
                ECCurve.NamedCurves.nistP256);
            const string keyId = "test-release-key";
            var fixture = new RecoveryFixture(
                directory,
                signingKey,
                keyId);
            var trustKey = SignedManifestCodec.ExportTrustKey(
                keyId,
                signingKey);
            File.WriteAllBytes(
                fixture.TrustBundlePath,
                ManifestJson.SerializeTrustBundle(
                    new ManifestTrustBundle(1, [trustKey])));
            File.WriteAllText(
                fixture.RecoveryPassphrasePath,
                Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(48)),
                new UTF8Encoding(false));
            BackupEnvelope.GenerateKeyPair(
                fixture.RecoveryPublicKeyPath,
                fixture.RecoveryPrivateKeyPath,
                fixture.RecoveryPassphrasePath);
            return fixture;
        }

        public void Dispose()
        {
            SigningKey.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
