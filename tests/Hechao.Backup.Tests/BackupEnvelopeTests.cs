using System.Security.Cryptography;
using Xunit;

namespace Hechao.Backup.Tests;

public sealed class BackupEnvelopeTests
{
    [Fact]
    public void EncryptDecrypt_RoundTripsMultipleAuthenticatedChunks()
    {
        using var fixture = BackupFixture.Create(
            BackupEnvelope.DefaultChunkSize + 37_111);

        var encryptedHeader = BackupEnvelope.Encrypt(
            fixture.PlaintextPath,
            fixture.EncryptedPath,
            fixture.PublicKeyPath);
        var inspectedHeader = BackupEnvelope.Inspect(
            fixture.EncryptedPath);
        var decryptedHeader = BackupEnvelope.Decrypt(
            fixture.EncryptedPath,
            fixture.RestoredPath,
            fixture.PrivateKeyPath,
            fixture.PassphrasePath);

        Assert.Equal(2, encryptedHeader.ChunkCount);
        Assert.Equal(encryptedHeader, inspectedHeader);
        Assert.Equal(encryptedHeader, decryptedHeader);
        Assert.Equal(
            BackupEnvelope.ComputeSha256(fixture.PlaintextPath),
            BackupEnvelope.ComputeSha256(fixture.RestoredPath));
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertextAndLeavesNoOutput()
    {
        using var fixture = BackupFixture.Create(256 * 1024);
        BackupEnvelope.Encrypt(
            fixture.PlaintextPath,
            fixture.EncryptedPath,
            fixture.PublicKeyPath);
        var bytes = File.ReadAllBytes(fixture.EncryptedPath);
        bytes[^24] ^= 0x5A;
        File.WriteAllBytes(fixture.TamperedPath, bytes);

        Assert.ThrowsAny<CryptographicException>(() =>
            BackupEnvelope.Decrypt(
                fixture.TamperedPath,
                fixture.RestoredPath,
                fixture.PrivateKeyPath,
                fixture.PassphrasePath));
        Assert.False(File.Exists(fixture.RestoredPath));
    }

    [Fact]
    public void Decrypt_RejectsWrongPrivateKey()
    {
        using var fixture = BackupFixture.Create(128 * 1024);
        using var other = BackupFixture.Create(0);
        BackupEnvelope.Encrypt(
            fixture.PlaintextPath,
            fixture.EncryptedPath,
            fixture.PublicKeyPath);

        Assert.ThrowsAny<CryptographicException>(() =>
            BackupEnvelope.Decrypt(
                fixture.EncryptedPath,
                fixture.RestoredPath,
                other.PrivateKeyPath,
                other.PassphrasePath));
        Assert.False(File.Exists(fixture.RestoredPath));
    }

    [Fact]
    public void Inspect_RejectsUnknownEnvelope()
    {
        using var fixture = BackupFixture.Create(64);

        Assert.Throws<InvalidDataException>(() =>
            BackupEnvelope.Inspect(fixture.PlaintextPath));
    }

    [Theory]
    [InlineData("backups/database/2026/07/example.hcbackup", true)]
    [InlineData("backups/services/2026/07/platform-data.hcbackup", true)]
    [InlineData("backups/recovery/signing-key-v1/key.hcbackup", true)]
    [InlineData("backups//database/example.hcbackup", false)]
    [InlineData("../backup.hcbackup", false)]
    [InlineData("/backups/database/example.hcbackup", false)]
    [InlineData("backups\\database\\example.hcbackup", false)]
    public void ObjectKeyValidation_RejectsAmbiguousPaths(
        string key,
        bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(key, OssBackupClient.ValidateObjectKey(key));
        }
        else
        {
            Assert.Throws<ArgumentException>(() =>
                OssBackupClient.ValidateObjectKey(key));
        }
    }

    private sealed class BackupFixture : IDisposable
    {
        private BackupFixture(string root)
        {
            Root = root;
            PlaintextPath = Path.Combine(root, "database.dump");
            EncryptedPath = Path.Combine(root, "database.hcbackup");
            TamperedPath = Path.Combine(root, "tampered.hcbackup");
            RestoredPath = Path.Combine(root, "restored.dump");
            PublicKeyPath = Path.Combine(root, "recovery-public.pem");
            PrivateKeyPath = Path.Combine(root, "recovery-private.p8");
            PassphrasePath = Path.Combine(root, "recovery-passphrase.txt");
        }

        internal string Root { get; }
        internal string PlaintextPath { get; }
        internal string EncryptedPath { get; }
        internal string TamperedPath { get; }
        internal string RestoredPath { get; }
        internal string PublicKeyPath { get; }
        internal string PrivateKeyPath { get; }
        internal string PassphrasePath { get; }

        internal static BackupFixture Create(int plaintextBytes)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"hechao-backup-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new BackupFixture(root);
            File.WriteAllText(
                fixture.PassphrasePath,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(48)));
            BackupEnvelope.GenerateKeyPair(
                fixture.PublicKeyPath,
                fixture.PrivateKeyPath,
                fixture.PassphrasePath);
            var plaintext = RandomNumberGenerator.GetBytes(plaintextBytes);
            File.WriteAllBytes(fixture.PlaintextPath, plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            return fixture;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }
}
