using System.Net.Sockets;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.StatusCollector;

public sealed class ServerHeartbeatCollector
{
    private readonly IMinecraftStatusClient _statusClient;
    private readonly IServerProcessMetricsProvider _processMetricsProvider;
    private readonly IServerAgentMetricsReader _agentMetricsReader;
    private readonly TimeProvider _timeProvider;

    public ServerHeartbeatCollector(IMinecraftStatusClient statusClient)
        : this(
            statusClient,
            NullServerProcessMetricsProvider.Instance,
            NullServerAgentMetricsReader.Instance,
            TimeProvider.System)
    {
    }

    public ServerHeartbeatCollector(
        IMinecraftStatusClient statusClient,
        IServerProcessMetricsProvider processMetricsProvider,
        IServerAgentMetricsReader agentMetricsReader,
        TimeProvider timeProvider)
    {
        _statusClient = statusClient;
        _processMetricsProvider = processMetricsProvider;
        _agentMetricsReader = agentMetricsReader;
        _timeProvider = timeProvider;
    }

    public async Task<ServerHeartbeatBatchRequest> CollectAsync(
        CollectorConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var capturedAt = _timeProvider.GetUtcNow();
        var timeout = TimeSpan.FromSeconds(configuration.ProbeTimeoutSeconds);
        var tasks = configuration.Servers.Select(
            server => ProbeAsync(
                server,
                capturedAt,
                timeout,
                TimeSpan.FromSeconds(configuration.MetricsMaxAgeSeconds),
                cancellationToken));
        var servers = await Task.WhenAll(tasks);
        return new ServerHeartbeatBatchRequest(
            capturedAt,
            configuration.CollectorInstance,
            servers);
    }

    private async Task<VelocityTargetHeartbeat> ProbeAsync(
        ServerProbeConfiguration server,
        DateTimeOffset capturedAt,
        TimeSpan timeout,
        TimeSpan metricsMaximumAge,
        CancellationToken cancellationToken)
    {
        var processTask = _processMetricsProvider.ProbeAsync(
            server,
            cancellationToken);
        var agentTask = _agentMetricsReader.ProbeAsync(
            server,
            capturedAt,
            metricsMaximumAge,
            cancellationToken);
        var issues = new List<ServerMetricIssueCode>();
        var online = false;
        var onlinePlayers = 0;
        var maxPlayers = server.FallbackMaxPlayers;
        string? softwareVersion = null;
        int? protocolVersion = null;

        try
        {
            var status = await _statusClient.QueryAsync(
                server.Host,
                server.Port,
                timeout,
                cancellationToken);
            online = true;
            onlinePlayers = status.OnlinePlayers;
            maxPlayers = status.MaxPlayers;
            softwareVersion = status.SoftwareVersion;
            protocolVersion = status.ProtocolVersion;
            Console.WriteLine(
                $"target={server.VelocityTarget} status=online players={status.OnlinePlayers}/{status.MaxPlayers}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            issues.Add(ServerMetricIssueCode.StatusTimeout);
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
            issues.Add(ServerMetricIssueCode.StatusUnavailable);
            Console.WriteLine(
                $"target={server.VelocityTarget} status=offline reason={exception.GetType().Name}");
        }

        var processResult = await processTask;
        issues.AddRange(processResult.Issues);
        var agentResult = await agentTask;
        var acceptsPausedSnapshot =
            online &&
            onlinePlayers == 0 &&
            server.AllowStaleMetricsWhenEmpty &&
            agentResult.Metrics is not null &&
            agentResult.Issue == ServerMetricIssueCode.MetricsFileStale;
        if (agentResult.Issue is not null && !acceptsPausedSnapshot)
        {
            issues.Add(agentResult.Issue.Value);
        }

        var process = processResult.Process;
        var metrics = agentResult.Issue == ServerMetricIssueCode.MetricsFileStale
                ? null
                : agentResult.Metrics;
        return new VelocityTargetHeartbeat(
            server.VelocityTarget,
            online,
            onlinePlayers,
            maxPlayers,
            softwareVersion,
            protocolVersion,
            process?.WorkingSetBytes,
            process?.PrivateBytes,
            process?.CpuPercent,
            process?.StartedAt,
            processResult.DiskFreeBytes,
            processResult.DiskTotalBytes,
            metrics?.Tps1m,
            metrics?.Tps5m,
            metrics?.Tps15m,
            metrics?.MsptAverage,
            metrics?.GcCollectionTimeMilliseconds,
            metrics?.CapturedAt,
            issues.Distinct().ToArray());
    }
}
