using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed partial class DynamicDeploymentSlotProvisioner(
    ServerControlAgentConfiguration configuration,
    DynamicDeploymentSlotStore store,
    ServerTargetRegistry registry,
    Func<ServerControlTargetConfiguration, ServerTargetRuntime> runtimeFactory,
    IProcessRunner processRunner,
    string backupRoot,
    string runtimeMarkerDirectory)
{
    private const string OwnerMarkerName = ".hechao-slot-owner";
    private readonly SemaphoreSlim gate = new(1, 1);

    internal async Task<AgentCommandResult> ProvisionAsync(
        ServerDeploymentSlotProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        if (!configuration.DeploymentSlotProvisioning.Enabled)
        {
            return Failed(
                "SLOT_PROVISIONING_DISABLED",
                "本机代理未启用动态部署槽创建。 ");
        }

        if (!IsValid(request) ||
            !string.Equals(
                request.TemplateServerId,
                configuration.DeploymentSlotProvisioning.TemplateServerId,
                StringComparison.Ordinal))
        {
            return Failed(
                "INVALID_SLOT_PROVISIONING",
                "部署槽创建参数超出本机允许范围。 ");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = registry.Find(request.ServerId);
            if (existing is not null)
            {
                return existing.Configuration.RequireDeployedPackage &&
                       store.Contains(request.ServerId)
                    ? Succeeded(
                        "SLOT_ALREADY_PROVISIONED",
                        "动态部署槽已经存在，保持停止。 ")
                    : Failed(
                        "SLOT_ID_CONFLICT",
                        "该服务器 ID 已被非动态目标占用。 ");
            }

            var template = registry.Find(request.TemplateServerId);
            if (template is null ||
                !template.Configuration.PackageDeploymentEnabled)
            {
                return Failed(
                    "SLOT_TEMPLATE_UNAVAILABLE",
                    "本机未找到已批准的部署槽模板。 ");
            }

            var target = CreateTarget(request.ServerId, template.Configuration);
            try
            {
                configuration.ValidateDynamicTargets(
                    store.Snapshot().Append(target).ToArray());
            }
            catch (InvalidDataException exception)
            {
                return Failed(
                    "SLOT_CONFIGURATION_REJECTED",
                    AgentLog.Sanitize(exception.Message, 1200));
            }

            return await ProvisionCoreAsync(
                target,
                template.Configuration,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AgentCommandResult> ProvisionCoreAsync(
        ServerControlTargetConfiguration target,
        ServerControlTargetConfiguration template,
        CancellationToken cancellationToken)
    {
        var directoryCreated = false;
        var taskCreationAttempted = false;
        var storeAdded = false;
        try
        {
            EnsureRootIsSafe();
            if (Directory.Exists(target.ServerDirectory) ||
                File.Exists(target.ServerDirectory))
            {
                return Failed(
                    "SLOT_DIRECTORY_EXISTS",
                    "目标部署槽目录已存在，未覆盖任何文件。 ");
            }

            if (await ScheduledTaskExistsAsync(
                    target.StartTaskName,
                    cancellationToken))
            {
                return Failed(
                    "SLOT_TASK_EXISTS",
                    "目标部署槽计划任务已存在，未覆盖现有任务。 ");
            }

            Directory.CreateDirectory(target.ServerDirectory);
            directoryCreated = true;
            await File.WriteAllTextAsync(
                Path.Combine(target.ServerDirectory, OwnerMarkerName),
                target.ServerId,
                cancellationToken);

            new HostManagedSnapshotStore(template, backupRoot)
                .CopyInto(target.ServerDirectory);
            await WritePlaceholderFilesAsync(target, cancellationToken);
            new HostManagedSnapshotStore(target, backupRoot)
                .CaptureFromServer();

            taskCreationAttempted = true;
            var install = await processRunner.RunAsync(
                "pwsh.exe",
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    configuration.DeploymentSlotProvisioning.TaskInstallerScript,
                    "-ServerName",
                    target.ServerId,
                    "-ServerId",
                    target.ServerId,
                    "-ServerDirectory",
                    target.ServerDirectory,
                    "-StartScript",
                    target.StartScriptRelativePath,
                    "-BackupRoot",
                    backupRoot,
                    "-RuntimeMarkerDirectory",
                    runtimeMarkerDirectory
                ],
                TimeSpan.FromMinutes(1),
                cancellationToken);
            if (install.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "计划任务创建失败：" +
                    AgentLog.Sanitize(install.StandardError, 800));
            }

            store.Add(target);
            storeAdded = true;
            if (!registry.TryAdd(runtimeFactory(target)))
            {
                throw new InvalidOperationException(
                    "动态目标注册时出现重复服务器 ID。");
            }

            return Succeeded(
                "SLOT_PROVISIONED",
                "动态部署槽已创建并纳入服控，保持停止，等待整合包部署。 ");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync(
                target,
                directoryCreated,
                taskCreationAttempted,
                storeAdded);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                NotSupportedException or TimeoutException)
        {
            await RollbackAsync(
                target,
                directoryCreated,
                taskCreationAttempted,
                storeAdded);

            return Failed(
                "SLOT_PROVISIONING_FAILED",
                AgentLog.Sanitize(
                    $"部署槽创建失败，已执行回滚：{exception.Message}",
                    1800));
        }
    }

    private async Task RollbackAsync(
        ServerControlTargetConfiguration target,
        bool directoryCreated,
        bool taskCreationAttempted,
        bool storeAdded)
    {
        if (storeAdded)
        {
            TryRollback(() => store.Remove(target.ServerId));
        }

        if (taskCreationAttempted)
        {
            await TryDeleteTaskAsync(target.StartTaskName);
        }

        if (directoryCreated)
        {
            TryRollback(() => DeleteOwnedDirectory(target));
            TryRollback(() => DeleteSnapshot(target.ServerId));
        }
    }

    private ServerControlTargetConfiguration CreateTarget(
        string serverId,
        ServerControlTargetConfiguration template)
    {
        var root = configuration.DeploymentSlotProvisioning.GetNormalizedRoot();
        return (template with
        {
            ServerId = serverId,
            ServerDirectory = Path.Combine(root, serverId),
            StartTaskName = "Hechao-Server-" + serverId,
            PackageDeploymentEnabled = true,
            ServerDeletionEnabled = true,
            RequireDeployedPackage = true
        }).Normalize();
    }

    private void EnsureRootIsSafe()
    {
        var root = configuration.DeploymentSlotProvisioning.GetNormalizedRoot();
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The dynamic deployment slot root cannot be a reparse point.");
        }
    }

    private async Task<bool> ScheduledTaskExistsAsync(
        string taskName,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Query", "/TN", taskName],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task WritePlaceholderFilesAsync(
        ServerControlTargetConfiguration target,
        CancellationToken cancellationToken)
    {
        var properties = target.GetContainedDeploymentPath(
            target.PropertiesRelativePath);
        var memory = target.GetContainedDeploymentPath(
            target.MemorySettingsRelativePath);
        var start = target.GetContainedDeploymentPath(
            target.StartScriptRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(properties)!);
        Directory.CreateDirectory(Path.GetDirectoryName(memory)!);
        Directory.CreateDirectory(Path.GetDirectoryName(start)!);
        await File.WriteAllTextAsync(
            properties,
            $"server-port={target.Port}\r\nmax-players=20\r\n" +
            "view-distance=10\r\nsimulation-distance=10\r\n" +
            "difficulty=normal\r\nwhite-list=false\r\n",
            cancellationToken);
        await File.WriteAllTextAsync(
            memory,
            "-Xms1024M\r\n-Xmx1024M\r\n",
            cancellationToken);
        await File.WriteAllTextAsync(
            start,
            "@echo off\r\nif not defined HECHAO_MANAGED_START pause\r\n" +
            "echo No package has been deployed to this slot.\r\nexit /b 1\r\n",
            cancellationToken);
    }

    private async Task TryDeleteTaskAsync(string taskName)
    {
        try
        {
            await processRunner.RunAsync(
                "schtasks.exe",
                ["/Delete", "/TN", taskName, "/F"],
                TimeSpan.FromSeconds(15),
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or TimeoutException)
        {
        }
    }

    private void DeleteOwnedDirectory(ServerControlTargetConfiguration target)
    {
        var marker = Path.Combine(target.ServerDirectory, OwnerMarkerName);
        if (!File.Exists(marker) ||
            !string.Equals(
                File.ReadAllText(marker).Trim(),
                target.ServerId,
                StringComparison.Ordinal))
        {
            return;
        }

        DeleteSafeTree(target.ServerDirectory);
    }

    private void DeleteSnapshot(string serverId)
    {
        var path = Path.Combine(backupRoot, "host-managed", serverId);
        if (Directory.Exists(path))
        {
            DeleteSafeTree(path);
        }
    }

    private static void DeleteSafeTree(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "A provisioning rollback path cannot be a reparse point.");
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "A provisioning rollback tree cannot contain a reparse point.");
            }

            if (entry is DirectoryInfo child)
            {
                DeleteSafeTree(child.FullName);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        directory.Attributes = FileAttributes.Normal;
        directory.Delete();
    }

    private static void TryRollback(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                NotSupportedException)
        {
        }
    }

    private static bool IsValid(ServerDeploymentSlotProvisioningRequest request) =>
        DynamicSlotId().IsMatch(request.ServerId ?? string.Empty) &&
        ConfigurationPatterns.ServerId().IsMatch(
            request.TemplateServerId ?? string.Empty) &&
        !string.IsNullOrWhiteSpace(request.DisplayName) &&
        request.DisplayName.Trim().Length is >= 2 and <= 80 &&
        !request.DisplayName.Any(char.IsControl);

    private static AgentCommandResult Succeeded(string code, string message) =>
        new(ServerControlCommandOutcome.Succeeded, code, message);

    private static AgentCommandResult Failed(string code, string message) =>
        new(ServerControlCommandOutcome.Failed, code, message);

    [GeneratedRegex("^activity-[a-z0-9][a-z0-9-]{1,39}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DynamicSlotId();
}
