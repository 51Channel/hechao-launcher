using System.Security.Cryptography;
using Hechao.Api.Distribution;
using Hechao.Distribution;

namespace Hechao.Api.Tests;

public sealed class ProfileReleaseManifestValidatorTests
{
    [Fact]
    public void Validate_VerifiesSignatureProfileAndComputedMetadata()
    {
        const string profileId = "activity-neoforge-1.21.11";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var objectDigest = Convert.ToHexString(
            SHA256.HashData("object"u8)).ToLowerInvariant();
        var manifest = CreateManifest(profileId, objectDigest);
        var envelope = ManifestJson.SerializeEnvelope(
            SignedManifestCodec.Sign(manifest, "test-key", key));
        var trust = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("test-key", key)]);

        var result = ProfileReleaseManifestValidator.Validate(
            envelope,
            profileId,
            trust);

        Assert.Equal(profileId, result.ProfileId);
        Assert.Equal("1.0.10", result.Version);
        Assert.Equal(6, result.DownloadBytes);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant(),
            result.ManifestSha256);
    }

    [Fact]
    public void Validate_RejectsDifferentProfile()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var objectDigest = Convert.ToHexString(
            SHA256.HashData("object"u8)).ToLowerInvariant();
        var envelope = ManifestJson.SerializeEnvelope(
            SignedManifestCodec.Sign(
                CreateManifest("base-1.21.11", objectDigest),
                "test-key",
                key));
        var trust = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("test-key", key)]);

        Assert.Throws<ManifestIntegrityException>(() =>
            ProfileReleaseManifestValidator.Validate(
                envelope,
                "activity-neoforge-1.21.11",
                trust));
    }

    [Fact]
    public void EmbeddedTrustBundle_MatchesProductionKey()
    {
        var provider = new DistributionTrustBundleProvider();

        var key = Assert.Single(provider.TrustBundle.Keys);
        Assert.Equal("release-2026-07-primary", key.KeyId);
        Assert.Equal(SignedManifestCodec.Algorithm, key.Algorithm);
    }

    private static ClientManifest CreateManifest(
        string profileId,
        string objectDigest)
    {
        return new ClientManifest(
            ManifestValidator.CurrentSchemaVersion,
            profileId,
            "1.0.10",
            "1.21.11",
            "21",
            "NeoForge",
            "21.11.42",
            DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
            [
                new ClientManifestFile(
                    "mods/example.jar",
                    6,
                    objectDigest,
                    $"https://launcher-api.hechao.world/v1/profiles/{profileId}/objects/{objectDigest[..2]}/{objectDigest}")
            ],
            []);
    }
}
