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
    private readonly object _activeDeploymentLock = new();
    private Guid? _activeDeploymentCommandId;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        receipts.Cleanup();
        CleanupPackageCache();
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
                captured,
                GetActiveDeploymentCommandIds()),
            cancellationToken);
    }

    private async Task ProcessCommandAsync(
        ServerControlCommandDelivery command,
        CancellationToken cancellationToken)
    {
        var tracksDeployment =
            command.Kind == ServerControlCommandKind.DeployPackage;
        if (tracksDeployment)
        {
            SetActiveDeployment(command.CommandId);
        }

        try
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
                    : await ExecuteCommandAsync(
                        target,
                        command,
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
        finally
        {
            if (tracksDeployment)
            {
                ClearActiveDeployment(command.CommandId);
            }
        }
    }

    private async Task<AgentCommandResult> ExecuteCommandAsync(
        ServerTargetRuntime target,
        ServerControlCommandDelivery command,
        CancellationToken cancellationToken)
    {
        if (command.Kind != ServerControlCommandKind.DeployPackage)
        {
            return await target.ExecuteAsync(
                command,
                targets,
                cancellationToken);
        }

        if (command.PackageDeployment is null)
        {
            return new AgentCommandResult(
                ServerControlCommandOutcome.Failed,
                "PACKAGE_METADATA_MISSING",
                "部署命令缺少不可变整合包元数据。 ");
        }

        try
        {
            var archivePath = GetPackageArchivePath(
                command.PackageDeployment.ArchiveSha256);
            await apiClient.DownloadPackageArchiveAsync(
                command,
                archivePath,
                cancellationToken);
            return await target.ExecuteAsync(
                command,
                targets,
                cancellationToken,
                archivePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or HttpRequestException or
                System.Security.Cryptography.CryptographicException or
                OverflowException or TimeoutException)
        {
            return new AgentCommandResult(
                ServerControlCommandOutcome.Failed,
                "PACKAGE_DOWNLOAD_FAILED",
                AgentLog.Sanitize(
                    $"服务端整合包下载或校验失败：{exception.Message}",
                    1800));
        }
    }

    private string GetPackageArchivePath(string sha256)
    {
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "The package archive digest is invalid.");
        }

        var root = Path.GetFullPath(Path.Combine(
                configuration.StateDirectory,
                "package-cache"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The package cache directory cannot be a reparse point.");
        }

        var path = Path.GetFullPath(Path.Combine(
            root,
            sha256.ToLowerInvariant() + ".zip"));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The package cache path escaped its configured root.");
        }

        return path;
    }

    private void CleanupPackageCache()
    {
        var root = Path.Combine(configuration.StateDirectory, "package-cache");
        if (!Directory.Exists(root))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.zip"))
        {
            try
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private IReadOnlyList<Guid> GetActiveDeploymentCommandIds()
    {
        lock (_activeDeploymentLock)
        {
            return _activeDeploymentCommandId is { } commandId
                ? [commandId]
                : [];
        }
    }

    private void SetActiveDeployment(Guid commandId)
    {
        lock (_activeDeploymentLock)
        {
            if (_activeDeploymentCommandId is not null)
            {
                throw new InvalidOperationException(
                    "Only one package deployment can be active per agent.");
            }

            _activeDeploymentCommandId = commandId;
        }
    }

    private void ClearActiveDeployment(Guid commandId)
    {
        lock (_activeDeploymentLock)
        {
            if (_activeDeploymentCommandId == commandId)
            {
                _activeDeploymentCommandId = null;
            }
        }
    }
}
