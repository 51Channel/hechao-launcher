using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hechao.Distribution;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Distribution;

public sealed partial class ProfileManifestStore(
    IOptions<DistributionOptions> options,
    IMemoryCache cache,
    ILogger<ProfileManifestStore> logger)
{
    public async Task<StoredProfileManifest> StoreReleaseAsync(
        string profileId,
        string expectedEnvelopeSha256,
        byte[] envelope,
        CancellationToken cancellationToken)
    {
        if (!ProfileIdRegex().IsMatch(profileId) ||
            !Sha256Regex().IsMatch(expectedEnvelopeSha256) ||
            envelope.Length is 0 ||
            envelope.Length > options.Value.MaximumManifestBytes ||
            string.IsNullOrWhiteSpace(options.Value.ManifestDirectory))
        {
            throw new ArgumentException("The profile release manifest is invalid.");
        }

        var normalizedDigest = expectedEnvelopeSha256.ToLowerInvariant();
        var actualDigest = Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant();
        if (!string.Equals(actualDigest, normalizedDigest, StringComparison.Ordinal))
        {
            throw new ManifestIntegrityException(
                "The profile release manifest digest changed before storage.");
        }

        var path = GetImmutableManifestPath(profileId, normalizedDigest);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(existing),
                    SHA256.HashData(envelope)))
            {
                throw new IOException(
                    "An immutable manifest path already contains different data.");
            }

            return new StoredProfileManifest(path, Created: false);
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{normalizedDigest}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(envelope, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            var created = true;
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                var existing = await File.ReadAllBytesAsync(path, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(existing),
                        SHA256.HashData(envelope)))
                {
                    throw;
                }

                created = false;
            }

            return new StoredProfileManifest(path, created);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public void DeleteStoredRelease(StoredProfileManifest storedManifest)
    {
        if (!storedManifest.Created)
        {
            return;
        }

        try
        {
            File.Delete(storedManifest.Path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Unable to remove an uncommitted profile release manifest.");
        }
    }

    public async Task<PublishedProfileManifest?> ReadPublishedAsync(
        string profileId,
        string expectedEnvelopeSha256,
        CancellationToken cancellationToken)
    {
        if (!ProfileIdRegex().IsMatch(profileId) ||
            !Sha256Regex().IsMatch(expectedEnvelopeSha256) ||
            string.IsNullOrWhiteSpace(options.Value.ManifestDirectory))
        {
            return null;
        }

        var normalizedDigest = expectedEnvelopeSha256.ToLowerInvariant();
        var cacheKey = $"published-manifest:{profileId}:{normalizedDigest}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            var envelope = await ReadEnvelopeAsync(
                profileId,
                normalizedDigest,
                cancellationToken);
            if (envelope is null)
            {
                return null;
            }

            try
            {
                var manifest = SignedManifestCodec.ReadDigestAnchored(envelope, normalizedDigest);
                if (!string.Equals(manifest.ProfileId, profileId, StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "Published manifest profile mismatch for {ProfileId}: payload contains {PayloadProfileId}.",
                        profileId,
                        manifest.ProfileId);
                    return null;
                }

                var objectDigests = manifest.Files
                    .Select(file => file.Sha256.ToLowerInvariant())
                    .ToFrozenSet(StringComparer.Ordinal);
                return new PublishedProfileManifest(envelope, objectDigests);
            }
            catch (Exception exception) when (
                exception is ManifestFormatException or ManifestIntegrityException)
            {
                logger.LogWarning(exception, "Published manifest validation failed for {ProfileId}.", profileId);
                return null;
            }
        });
    }

    private async Task<byte[]?> ReadEnvelopeAsync(
        string profileId,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Value.ManifestDirectory);
        var immutablePath = GetImmutableManifestPath(profileId, expectedDigest);
        var envelope = await ReadEnvelopeFileAsync(
            root,
            immutablePath,
            cancellationToken);
        if (envelope is not null)
        {
            return envelope;
        }

        return await ReadEnvelopeFileAsync(
            root,
            Path.GetFullPath(Path.Combine(root, profileId + ".json")),
            cancellationToken);
    }

    private async Task<byte[]?> ReadEnvelopeFileAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 || file.Length > options.Value.MaximumManifestBytes)
        {
            return null;
        }

        var result = new byte[file.Length];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.ReadExactlyAsync(result, cancellationToken);
        return result;
    }

    private string GetImmutableManifestPath(string profileId, string digest)
    {
        var root = Path.GetFullPath(options.Value.ManifestDirectory);
        return Path.GetFullPath(Path.Combine(
            root,
            "releases",
            profileId,
            digest + ".json"));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

public sealed record PublishedProfileManifest(
    byte[] Envelope,
    IReadOnlySet<string> ObjectDigests);

public sealed record StoredProfileManifest(string Path, bool Created);
