using System.Reflection;
using Hechao.Distribution;

namespace Hechao.Api.Distribution;

public sealed record ValidatedProfileReleaseManifest(
    byte[] Envelope,
    string ProfileId,
    string Version,
    string ManifestSha256,
    long DownloadBytes,
    int FileCount,
    string MinecraftVersion,
    string JavaVersion,
    string Loader,
    string LoaderVersion,
    DateTimeOffset PublishedAt,
    string KeyId);

public sealed class DistributionTrustBundleProvider
{
    private const string ResourceName =
        "Hechao.Api.Distribution.distribution-trust.json";

    public DistributionTrustBundleProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "The distribution trust bundle is missing from the API release.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        TrustBundle = ManifestJson.DeserializeTrustBundle(memory.ToArray());
    }

    public ManifestTrustBundle TrustBundle { get; }
}

public static class ProfileReleaseManifestValidator
{
    public static ValidatedProfileReleaseManifest Validate(
        byte[] envelope,
        string expectedProfileId,
        ManifestTrustBundle trustBundle)
    {
        if (!Admin.AdminProfileReleaseRules.IsValidProfileId(expectedProfileId))
        {
            throw new ManifestFormatException("The expected profile ID is invalid.");
        }

        var verified = SignedManifestCodec.Verify(envelope, trustBundle);
        var manifest = verified.Manifest;
        if (!string.Equals(
                manifest.ProfileId,
                expectedProfileId,
                StringComparison.Ordinal))
        {
            throw new ManifestIntegrityException(
                "The signed manifest belongs to a different client profile.");
        }

        long downloadBytes = 0;
        foreach (var file in manifest.Files)
        {
            downloadBytes = checked(downloadBytes + file.Size);
            ValidateObjectUrl(file, expectedProfileId);
        }

        return new ValidatedProfileReleaseManifest(
            envelope,
            manifest.ProfileId,
            manifest.Version,
            verified.EnvelopeSha256,
            downloadBytes,
            manifest.Files.Count,
            manifest.MinecraftVersion,
            manifest.JavaVersion,
            manifest.Loader,
            manifest.LoaderVersion,
            manifest.PublishedAt.ToUniversalTime(),
            verified.KeyId);
    }

    private static void ValidateObjectUrl(
        ClientManifestFile file,
        string profileId)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestIntegrityException(
                $"Manifest object URL is not HTTPS: {file.Path}");
        }

        var expectedSuffix =
            $"/v1/profiles/{profileId}/objects/{file.Sha256[..2]}/{file.Sha256}";
        if (!uri.AbsolutePath.EndsWith(
                expectedSuffix,
                StringComparison.Ordinal))
        {
            throw new ManifestIntegrityException(
                $"Manifest object URL does not match its digest: {file.Path}");
        }
    }
}
