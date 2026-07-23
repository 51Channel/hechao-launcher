using System.Security.Cryptography;

namespace Hechao.Distribution.Tests;

public sealed class SignedManifestCodecTests
{
    [Fact]
    public void Verify_AcceptsPayloadSignedByTrustedKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestTestData.CreateManifest("trusted"u8.ToArray());
        var envelope = SignedManifestCodec.Sign(manifest, "release-2026", key);
        var envelopeJson = ManifestJson.SerializeEnvelope(envelope);
        var trustBundle = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("release-2026", key)]);

        var verified = SignedManifestCodec.Verify(envelopeJson, trustBundle);

        Assert.Equal(manifest.ProfileId, verified.Manifest.ProfileId);
        Assert.Equal(manifest.Version, verified.Manifest.Version);
        Assert.Equal(manifest.PublishedAt, verified.Manifest.PublishedAt);
        Assert.Equal(manifest.Files, verified.Manifest.Files);
        Assert.Equal(manifest.DeletePaths, verified.Manifest.DeletePaths);
        Assert.Equal("release-2026", verified.KeyId);
        Assert.Matches("^[0-9a-f]{64}$", verified.EnvelopeSha256);
    }

    [Fact]
    public void Verify_RejectsPayloadChangedAfterSigning()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestTestData.CreateManifest("trusted"u8.ToArray());
        var envelope = SignedManifestCodec.Sign(manifest, "release-2026", key);
        var changedManifest = manifest with { Version = "9.9.9" };
        var tamperedEnvelope = envelope with
        {
            PayloadBase64 = Convert.ToBase64String(ManifestJson.SerializeManifest(changedManifest))
        };
        var trustBundle = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("release-2026", key)]);

        Assert.Throws<ManifestSignatureException>(() =>
            SignedManifestCodec.Verify(ManifestJson.SerializeEnvelope(tamperedEnvelope), trustBundle));
    }

    [Fact]
    public void Verify_RejectsUnknownSigningKey()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = SignedManifestCodec.Sign(
            ManifestTestData.CreateManifest("trusted"u8.ToArray()),
            "release-2026",
            signingKey);
        var trustBundle = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("other-key", otherKey)]);

        Assert.Throws<ManifestSignatureException>(() =>
            SignedManifestCodec.Verify(ManifestJson.SerializeEnvelope(envelope), trustBundle));
    }

    [Fact]
    public void ReadDigestAnchored_AcceptsEnvelopeMatchingTrustedCatalogDigest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestTestData.CreateManifest("trusted"u8.ToArray());
        var envelope = SignedManifestCodec.Sign(manifest, "release-2026", key);
        var envelopeJson = ManifestJson.SerializeEnvelope(envelope);
        var digest = Convert.ToHexString(SHA256.HashData(envelopeJson)).ToLowerInvariant();

        var decoded = SignedManifestCodec.ReadDigestAnchored(envelopeJson, digest);

        Assert.Equal(manifest.ProfileId, decoded.ProfileId);
        Assert.Equal(manifest.Files, decoded.Files);
    }

    [Fact]
    public void ReadDigestAnchored_RejectsEnvelopeNotMatchingCatalogDigest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = SignedManifestCodec.Sign(
            ManifestTestData.CreateManifest("trusted"u8.ToArray()),
            "release-2026",
            key);

        Assert.Throws<ManifestIntegrityException>(() =>
            SignedManifestCodec.ReadDigestAnchored(
                ManifestJson.SerializeEnvelope(envelope),
                new string('0', 64)));
    }
}
