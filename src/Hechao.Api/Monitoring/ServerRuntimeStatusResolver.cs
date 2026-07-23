using Hechao.Contracts;

namespace Hechao.Api.Monitoring;

public sealed record ServerHeartbeatObservation(
    bool Online,
    int OnlinePlayers,
    int MaxPlayers,
    DateTimeOffset ReceivedAt);

public readonly record struct ResolvedServerRuntimeStatus(
    ServerStatus Status,
    int OnlinePlayers,
    int MaxPlayers);

public static class ServerRuntimeStatusResolver
{
    public static ResolvedServerRuntimeStatus Resolve(
        ServerStatus configuredStatus,
        int configuredOnlinePlayers,
        int configuredMaxPlayers,
        ServerHeartbeatObservation? heartbeat,
        DateTimeOffset now,
        TimeSpan freshness)
    {
        if (configuredStatus is ServerStatus.Maintenance or ServerStatus.Closed)
        {
            return new ResolvedServerRuntimeStatus(
                configuredStatus,
                0,
                configuredMaxPlayers);
        }

        if (heartbeat is null)
        {
            return new ResolvedServerRuntimeStatus(
                configuredStatus,
                configuredOnlinePlayers,
                configuredMaxPlayers);
        }

        var maxPlayers = heartbeat.MaxPlayers > 0
            ? heartbeat.MaxPlayers
            : configuredMaxPlayers;
        if (heartbeat.ReceivedAt < now.Subtract(freshness) || !heartbeat.Online)
        {
            return new ResolvedServerRuntimeStatus(ServerStatus.Closed, 0, maxPlayers);
        }

        return new ResolvedServerRuntimeStatus(
            ServerStatus.Online,
            heartbeat.OnlinePlayers,
            maxPlayers);
    }
}
