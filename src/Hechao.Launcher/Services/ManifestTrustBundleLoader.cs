using System.IO;
using System.Reflection;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public static class ManifestTrustBundleLoader
{
    private const int MaximumTrustBundleBytes = 1024 * 1024;
    private const string EmbeddedResourceName = "Hechao.Launcher.Assets.distribution-trust.json";

    public static ManifestTrustBundle LoadDefault()
    {
        var overridePath = Environment.GetEnvironmentVariable("HECHAO_DISTRIBUTION_TRUST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return ReadBundle(File.ReadAllBytes(Environment.ExpandEnvironmentVariables(overridePath)));
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new ManifestSignatureException("The embedded manifest trust bundle is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ReadBundle(memory.ToArray());
    }

    private static ManifestTrustBundle ReadBundle(byte[] content)
    {
        if (content.Length is 0 or > MaximumTrustBundleBytes)
        {
            throw new ManifestSignatureException("The manifest trust bundle has an invalid size.");
        }

        try
        {
            return ManifestJson.DeserializeTrustBundle(content);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ManifestSignatureException("The manifest trust bundle is malformed.", exception);
        }
    }
}
