namespace Hechao.Distribution;

public sealed record ClientManifest(
    int SchemaVersion,
    string ProfileId,
    string Version,
    string MinecraftVersion,
    string JavaVersion,
    string Loader,
    string LoaderVersion,
    DateTimeOffset PublishedAt,
    IReadOnlyList<ClientManifestFile> Files,
    IReadOnlyList<string> DeletePaths);

public sealed record ClientManifestFile(
    string Path,
    long Size,
    string Sha256,
    string Url,
    bool Required = true);

public sealed record SignedManifestEnvelope(
    int SchemaVersion,
    string Algorithm,
    string KeyId,
    string PayloadBase64,
    string SignatureBase64);

public sealed record ManifestTrustKey(
    string KeyId,
    string Algorithm,
    string PublicKeyBase64);

public sealed record ManifestTrustBundle(
    int SchemaVersion,
    IReadOnlyList<ManifestTrustKey> Keys);

public sealed record VerifiedClientManifest(
    ClientManifest Manifest,
    string EnvelopeSha256,
    string KeyId);
