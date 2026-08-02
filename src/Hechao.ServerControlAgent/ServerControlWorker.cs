using System.Reflection;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed class ServerControlWorker(
    ServerControlAgentConfiguration configuration,
    AgentApiClient apiClient,
    IReadOnlyList<ServerTargetRuntime> targets,
    CommandReceiptStore receipts,
    AgentLog log)
{
    private readonly string _version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ??
        "0.0.0";
    private readonly ResilientLoopRunner _loopRunner = new(log);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        receipts.Cleanup();
        await Task.WhenAll(
            _loopRunner.RunAsync(
                "heartbeat_failed",
                TimeSpan.FromSeconds(configuration.HeartbeatSeconds),
                SendHeartbeatAsync,
                cancellationToken),
            _loopRunner.RunAsync(
                "command_poll_failed",
                TimeSpan.FromSeconds(configuration.PollSeconds),
                PollCommandsAsync,
                cancellationToken));
    }

    private async Task PollCommandsAsync(CancellationToken cancellationToken)
    {
        var claim = await apiClient.ClaimAsync(
            1,
            cancellationToken);
        foreach (var command in claim.Commands)
        {
            await ProcessCommandAsync(command, cancellationToken);
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var captured = new List<ServerControlAgentTargetHeartbeat>(targets.Count);
        foreach (var target in targets)
        {
            captured.Add(await target.CaptureHeartbeatAsync(cancellationToken));
        }

        await apiClient.SendHeartbeatAsync(
            new ServerControlAgentHeartbeatRequest(
                configuration.AgentId,
                _version,
                DateTimeOffset.UtcNow,
                captured),
            cancellationToken);
    }

    private async Task ProcessCommandAsync(
        ServerControlCommandDelivery command,
        CancellationToken cancellationToken)
    {
        var receipt = receipts.TryRead(command.CommandId);
        AgentCommandResult result;
        if (receipt is not null)
        {
            result = receipt.Result;
            log.WriteBestEffort(
                "INFO",
                "command_replayed_from_receipt",
                command.CommandId.ToString("D"));
        }
        else
        {
            var target = targets.SingleOrDefault(item =>
                string.Equals(
                    item.Configuration.ServerId,
                    command.ServerId,
                    StringComparison.Ordinal));
            result = target is null
                ? new AgentCommandResult(
                    ServerControlCommandOutcome.Failed,
                    "TARGET_NOT_CONFIGURED",
                    "该服务器不在本机代理白名单中。")
                : await target.ExecuteAsync(
                    command,
                    targets,
                    cancellationToken);
            receipts.Save(command.CommandId, result);
        }

        await apiClient.CompleteAsync(
            command.CommandId,
            new ServerControlCommandCompletionRequest(
                configuration.AgentId,
                command.AttemptCount,
                result.Outcome,
                result.ResultCode,
                result.ResultMessage),
            cancellationToken);
        log.WriteBestEffort(
            result.Outcome == ServerControlCommandOutcome.Succeeded
                ? "INFO"
                : "ERROR",
            "command_completed",
            $"{command.CommandId:D} {command.ServerId} {command.Kind} " +
            result.ResultCode);
    }
}
