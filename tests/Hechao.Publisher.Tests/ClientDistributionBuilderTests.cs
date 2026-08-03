using System.Security.Cryptography;
using Hechao.Distribution;

namespace Hechao.Publisher.Tests;

public sealed class ClientDistributionBuilderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-distribution-builder-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildAsync_CreatesContentAddressedObjectsAndSignedManifest()
    {
        var source = Path.Combine(root, "source");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.Combine(source, "mods"));
        await File.WriteAllTextAsync(
            Path.Combine(source, "mods", "example.jar"),
            "example");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyPath = Path.Combine(root, "signing.pem");
        await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());

        var result = await ClientDistributionBuilder.BuildAsync(
            new ClientDistributionBuildOptions(
                source,
                output,
                "summer-fabric-1.20.1",
                "1.0.0",
                "1.20.1",
                "17",
                "Fabric",
                "0.16.14",
                "test-key",
                new SigningKeyInput(keyPath, null, null),
                new Uri(
                    "https://launcher-api.example/v1/profiles/summer-fabric-1.20.1/"),
                DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
                []));

        Assert.Equal(1, result.FileCount);
        Assert.True(File.Exists(result.ManifestPath));
        var trust = new ManifestTrustBundle(
            1,
            [SignedManifestCodec.ExportTrustKey("test-key", key)]);
        var verified = SignedManifestCodec.Verify(
            await File.ReadAllBytesAsync(result.ManifestPath),
            trust);
        var file = Assert.Single(verified.Manifest.Files);
        Assert.Equal("mods/example.jar", file.Path);
        Assert.Equal(result.ManifestSha256, verified.EnvelopeSha256);
        Assert.True(File.Exists(Path.Combine(
            output,
            "objects",
            file.Sha256[..2],
            file.Sha256)));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
