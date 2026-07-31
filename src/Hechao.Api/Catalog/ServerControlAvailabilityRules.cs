using Hechao.Contracts;

namespace Hechao.Api.Catalog;

public sealed record ServerControlObservation(
    bool Online,
    DateTimeOffset LastSeenAt);

public sealed record ResolvedServerControlAvailability(
    ServerStatus Status,
    bool HasTarget,
    bool IsFresh,
    bool? ReportedOnline,
    DateTimeOffset? LastSeenAt);

public static class ServerControlAvailabilityRules
{
    public static ResolvedServerControlAvailability Resolve(
        ServerStatus policyStatus,
        ServerControlObservation? observation,
        DateTimeOffset now,
        TimeSpan freshness)
    {
        if (observation is null)
        {
            return new ResolvedServerControlAvailability(
                policyStatus,
                HasTarget: false,
                IsFresh: false,
                ReportedOnline: null,
                LastSeenAt: null);
        }

        var isFresh = freshness > TimeSpan.Zero
            && observation.LastSeenAt >= now - freshness;
        var effectiveStatus = policyStatus;

        if (policyStatus == ServerStatus.Online
            && (!isFresh || !observation.Online))
        {
            effectiveStatus = ServerStatus.Closed;
        }

        return new ResolvedServerControlAvailability(
            effectiveStatus,
            HasTarget: true,
            IsFresh: isFresh,
            ReportedOnline: observation.Online,
            LastSeenAt: observation.LastSeenAt);
    }
}
