using System.Security.Cryptography;
using Hechao.Distribution;

namespace Hechao.Publisher.Tests;

public sealed class DistributionReleaseValidatorTests
{
    [Fact]
    public void Validate_AcceptsSignedReleaseWithExactObjectSet()
    {
        using var release = TestRelease.Create();

        var result = DistributionReleaseValidator.Validate(
            release.DistributionPath,
            release.ManifestPath,
            release.TrustBundlePath);

        Assert.Equal("activity-neoforge-1.21.11", result.ProfileId);
        Assert.Equal("1.0.10", result.Version);
        Assert.Equal("release-2026", result.KeyId);
        Assert.Equal(1, result.LogicalFileCount);
        Assert.Equal(release.Content.Length, result.LogicalBytes);
        Assert.Equal(1, result.ObjectCount);
        Assert.Equal(release.Content.Length, result.ObjectBytes);
    }

    [Fact]
    public void Validate_RejectsManifestObjectMissingFromDistribution()
    {
        using var release = TestRelease.Create();
        File.Delete(release.ObjectPath);

        Assert.Throws<PublisherUsageException>(() =>
            DistributionReleaseValidator.Validate(
                release.DistributionPath,
                release.ManifestPath,
                release.TrustBundlePath));
    }

    [Fact]
    public void Validate_RejectsUnreferencedDistributionObject()
    {
        using var release = TestRelease.Create();
        var extraContent = "stale-object"u8.ToArray();
        var extraDigest = Convert.ToHexString(
            SHA256.HashData(extraContent)).ToLowerInvariant();
        var extraDirectory = Path.Combine(
            release.DistributionPath,
            "objects",
            extraDigest[..2]);
        Directory.CreateDirectory(extraDirectory);
        File.WriteAllBytes(Path.Combine(extraDirectory, extraDigest), extraContent);

        Assert.Throws<PublisherUsageException>(() =>
            DistributionReleaseValidator.Validate(
                release.DistributionPath,
                release.ManifestPath,
                release.TrustBundlePath));
    }

    [Fact]
    public void Validate_RejectsObjectUrlThatDoesNotMatchDigest()
    {
        using var release = TestRelease.Create(useMismatchedObjectUrl: true);

        Assert.Throws<PublisherUsageException>(() =>
            DistributionReleaseValidator.Validate(
                release.DistributionPath,
                release.ManifestPath,
                release.TrustBundlePath));
    }

    private sealed class TestRelease(
        string rootPath,
        string distributionPath,
        string manifestPath,
        string trustBundlePath,
        string objectPath,
        byte[] content) : IDisposable
    {
        public string DistributionPath { get; } = distributionPath;
        public string ManifestPath { get; } = manifestPath;
        public string TrustBundlePath { get; } = trustBundlePath;
        public string ObjectPath { get; } = objectPath;
        public byte[] Content { get; } = content;

        public static TestRelease Create(bool useMismatchedObjectUrl = false)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            var distributionPath = Path.Combine(rootPath, "distribution");
            var content = "client-content"u8.ToArray();
            var digest = Convert.ToHexString(
                SHA256.HashData(content)).ToLowerInvariant();
            var objectDirectory = Path.Combine(
                distributionPath,
                "objects",
                digest[..2]);
            Directory.CreateDirectory(objectDirectory);
            var objectPath = Path.Combine(objectDirectory, digest);
            File.WriteAllBytes(objectPath, content);

            var manifest = new ClientManifest(
                ManifestValidator.CurrentSchemaVersion,
                "activity-neoforge-1.21.11",
                "1.0.10",
                "1.21.11",
                "21",
                "NeoForge",
                "21.11.42",
                DateTimeOffset.Parse("2026-07-24T11:26:21Z"),
                [
                    new ClientManifestFile(
                        "mods/activity.jar",
                        content.Length,
                        digest,
                        useMismatchedObjectUrl
                            ? "https://launcher-api.hechao.world/v1/profiles/" +
                              "activity-neoforge-1.21.11/objects/aa/" +
                              new string('a', 64)
                            : "https://launcher-api.hechao.world/v1/profiles/" +
                              $"activity-neoforge-1.21.11/objects/{digest[..2]}/{digest}")
                ],
                []);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var envelope = SignedManifestCodec.Sign(manifest, "release-2026", key);
            var manifestDirectory = Path.Combine(distributionPath, "manifests");
            Directory.CreateDirectory(manifestDirectory);
            var manifestPath = Path.Combine(
                manifestDirectory,
                "activity-neoforge-1.21.11.json");
            File.WriteAllBytes(
                manifestPath,
                ManifestJson.SerializeEnvelope(envelope));

            var trustBundle = new ManifestTrustBundle(
                1,
                [SignedManifestCodec.ExportTrustKey("release-2026", key)]);
            var trustBundlePath = Path.Combine(rootPath, "distribution-trust.json");
            File.WriteAllBytes(
                trustBundlePath,
                ManifestJson.SerializeTrustBundle(trustBundle));

            return new TestRelease(
                rootPath,
                distributionPath,
                manifestPath,
                trustBundlePath,
                objectPath,
                content);
        }

        public void Dispose()
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}
