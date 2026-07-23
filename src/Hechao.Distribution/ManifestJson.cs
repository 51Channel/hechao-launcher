using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hechao.Distribution;

public static class ManifestJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    public static byte[] SerializeManifest(ClientManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, CompactOptions);

    public static byte[] SerializeEnvelope(SignedManifestEnvelope envelope, bool writeIndented = true) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, writeIndented ? IndentedOptions : CompactOptions);

    public static byte[] SerializeTrustBundle(ManifestTrustBundle bundle, bool writeIndented = true) =>
        JsonSerializer.SerializeToUtf8Bytes(bundle, writeIndented ? IndentedOptions : CompactOptions);

    public static ClientManifest DeserializeManifest(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<ClientManifest>(json, CompactOptions)
        ?? throw new ManifestFormatException("The manifest payload is empty.");

    public static SignedManifestEnvelope DeserializeEnvelope(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<SignedManifestEnvelope>(json, CompactOptions)
        ?? throw new ManifestFormatException("The signed manifest envelope is empty.");

    public static ManifestTrustBundle DeserializeTrustBundle(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<ManifestTrustBundle>(json, CompactOptions)
        ?? throw new ManifestFormatException("The manifest trust bundle is empty.");

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = writeIndented
        };
    }
}

public class ManifestFormatException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed class ManifestSignatureException(string message, Exception? innerException = null)
    : ManifestFormatException(message, innerException);

public sealed class ManifestIntegrityException(string message, Exception? innerException = null)
    : IOException(message, innerException);
