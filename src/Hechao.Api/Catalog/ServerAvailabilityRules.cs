using Hechao.Contracts;

namespace Hechao.Api.Catalog;

public static class ServerAvailabilityRules
{
    public static ServerStatus ResolveStatus(
        ServerStatus configuredStatus,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        DateTimeOffset now)
    {
        if (configuredStatus != ServerStatus.Online)
        {
            return configuredStatus;
        }

        if (opensAt is not null && now < opensAt.Value)
        {
            return ServerStatus.Closed;
        }

        if (closesAt is not null && now >= closesAt.Value)
        {
            return ServerStatus.Closed;
        }

        return ServerStatus.Online;
    }
}
