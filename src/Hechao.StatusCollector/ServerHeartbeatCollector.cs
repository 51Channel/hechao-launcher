using System.Net.Sockets;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.StatusCollector;

public sealed class ServerHeartbeatCollector(IMinecraftStatusClient statusClient)
{
    public async Task<ServerHeartbeatBatchRequest> CollectAsync(
        CollectorConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(configuration.ProbeTimeoutSeconds);
        var tasks = configuration.Servers.Select(
            server => ProbeAsync(server, timeout, cancellationToken));
        var servers = await Task.WhenAll(tasks);
        return new ServerHeartbeatBatchRequest(
            DateTimeOffset.UtcNow,
            configuration.CollectorInstance,
            servers);
    }

    private async Task<VelocityTargetHeartbeat> ProbeAsync(
        ServerProbeConfiguration server,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await statusClient.QueryAsync(
                server.Host,
                server.Port,
                timeout,
                cancellationToken);
            Console.WriteLine(
                $"target={server.VelocityTarget} status=online players={status.OnlinePlayers}/{status.MaxPlayers}");
            return new VelocityTargetHeartbeat(
                server.VelocityTarget,
                true,
                status.OnlinePlayers,
                status.MaxPlayers,
                status.SoftwareVersion,
                status.ProtocolVersion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"target={server.VelocityTarget} status=offline reason=timeout");
        }
        catch (Exception exception) when (
            exception is IOException or
                SocketException or
                JsonException or
                InvalidOperationException or
                KeyNotFoundException or
                FormatException)
        {
            Console.WriteLine(
                $"target={server.VelocityTarget} status=offline reason={exception.GetType().Name}");
        }

        return new VelocityTargetHeartbeat(
            server.VelocityTarget,
            false,
            0,
            server.FallbackMaxPlayers,
            null,
            null);
    }
}
