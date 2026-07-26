using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Hechao.Contracts;

namespace Hechao.Api.Catalog;

public sealed record ClientProfileReleaseCandidate(
    ClientProfileReleaseChannel Channel,
    int RolloutPercentage,
    string Version,
    long DownloadBytes,
    string ManifestSha256,
    DateTimeOffset PublishedAt,
    bool IsPaused);

public static class ClientProfileReleaseResolver
{
    public static ClientProfileReleaseCandidate? Resolve(
        string profileId,
        Guid? userId,
        AccessTier? accessTier,
        IReadOnlyList<ClientProfileReleaseCandidate> candidates)
    {
        var byChannel = candidates
            .Where(item => !item.IsPaused)
            .ToDictionary(item => item.Channel);

        if (userId is not null &&
            accessTier == AccessTier.Administrator &&
            byChannel.TryGetValue(
                ClientProfileReleaseChannel.Test,
                out var testRelease) &&
            IsSelected(
                userId.Value,
                profileId,
                ClientProfileReleaseChannel.Test,
                testRelease.RolloutPercentage))
        {
            return testRelease;
        }

        if (userId is not null &&
            byChannel.TryGetValue(
                ClientProfileReleaseChannel.Gray,
                out var grayRelease) &&
            IsSelected(
                userId.Value,
                profileId,
                ClientProfileReleaseChannel.Gray,
                grayRelease.RolloutPercentage))
        {
            return grayRelease;
        }

        return byChannel.GetValueOrDefault(
            ClientProfileReleaseChannel.Production);
    }

    public static int GetStableBucket(
        Guid userId,
        string profileId,
        ClientProfileReleaseChannel channel)
    {
        var input = Encoding.UTF8.GetBytes(
            $"{userId:N}:{profileId}:{channel.ToString().ToLowerInvariant()}");
        var digest = SHA256.HashData(input);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(digest) % 100);
    }

    private static bool IsSelected(
        Guid userId,
        string profileId,
        ClientProfileReleaseChannel channel,
        int percentage)
    {
        return percentage >= 100 ||
               percentage > 0 &&
               GetStableBucket(userId, profileId, channel) < percentage;
    }
}
