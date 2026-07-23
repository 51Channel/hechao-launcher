using System.Collections.Frozen;
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
            var envelope = await ReadEnvelopeAsync(profileId, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Value.ManifestDirectory);
        var path = Path.GetFullPath(Path.Combine(root, profileId + ".json"));
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

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

public sealed record PublishedProfileManifest(
    byte[] Envelope,
    IReadOnlySet<string> ObjectDigests);
