using System.Security.Cryptography;
using Hechao.Api.Distribution;
using Hechao.Distribution;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class ProfileManifestStoreTests
{
    [Fact]
    public async Task ReadPublishedAsync_IndexesOnlyDigestAnchoredManifestObjects()
    {
        var directory = Directory.CreateTempSubdirectory("hechao-api-tests-");
        try
        {
            const string profileId = "activity-neoforge-1.21.11";
            var objectDigest = Convert.ToHexString(SHA256.HashData("object"u8)).ToLowerInvariant();
            var manifest = new ClientManifest(
                ManifestValidator.CurrentSchemaVersion,
                profileId,
                "1.0.0",
                "1.21.11",
                "21",
                "NeoForge",
                "21.11.42",
                DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
                [new ClientManifestFile(
                    "mods/example.jar",
                    6,
                    objectDigest,
                    $"https://launcher-api.hechao.world/v1/profiles/{profileId}/objects/{objectDigest[..2]}/{objectDigest}")],
                []);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var envelope = ManifestJson.SerializeEnvelope(
                SignedManifestCodec.Sign(manifest, "release-2026", key));
            var envelopeDigest = Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, profileId + ".json"), envelope);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = new ProfileManifestStore(
                Options.Create(new DistributionOptions { ManifestDirectory = directory.FullName }),
                cache,
                NullLogger<ProfileManifestStore>.Instance);

            var published = await store.ReadPublishedAsync(profileId, envelopeDigest, CancellationToken.None);
            var mismatched = await store.ReadPublishedAsync(profileId, new string('0', 64), CancellationToken.None);

            Assert.NotNull(published);
            Assert.Equal(envelope, published.Envelope);
            Assert.Contains(objectDigest, published.ObjectDigests);
            Assert.Null(mismatched);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
