using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hechao.Contracts;
using Hechao.Modpack;

namespace Hechao.ServerControlAgent;

internal sealed partial class ServerPackageDeployer(
    ServerControlTargetConfiguration configuration,
    string backupRoot,
    ServerDirectoryAccessGate? directoryAccessGate = null)
{
    private const int OwnerSchemaVersion = 1;
    internal const string DeploymentMarkerName = ".hechao-deployment.json";
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ServerDirectoryAccessGate directoryAccessGate =
        directoryAccessGate ?? new ServerDirectoryAccessGate();

    internal async Task<AgentCommandResult> DeployAsync(
        ServerPackageDeploymentRequest deployment,
        string archivePath,
        Func<CancellationToken, Task<int?>> findProcessIdAsync,
        CancellationToken cancellationToken)
    {
        if (!configuration.PackageDeploymentEnabled)
        {
            return Failed(
                "PACKAGE_DEPLOYMENT_DISABLED",
                "该服务端未在本机配置中启用整合包部署。 ");
        }

        if (!IsValid(deployment))
        {
            return Failed(
                "INVALID_PACKAGE_DEPLOYMENT",
                "整合包部署参数超出本机允许范围。 ");
        }

        if (await findProcessIdAsync(cancellationToken) is not null)
        {
            return Failed(
                "SERVER_STILL_RUNNING",
                "服务端仍在运行，已拒绝替换目录。 ");
        }

        var serverDirectory = Path.GetFullPath(configuration.ServerDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(serverDirectory);
        if (parent is null ||
            !Directory.Exists(parent.FullName) ||
            IsReparsePoint(parent.FullName))
        {
            return Failed(
                "SERVER_DIRECTORY_INVALID",
                "服务端目录或其父目录不存在，或属于重解析点。 ");
        }

        var directoryName = Path.GetFileName(serverDirectory);
        var stagingDirectory = Path.Combine(
            parent.FullName,
            $".{directoryName}.hechao-staging-{deployment.ImportId:N}");
        var rollbackDirectory = Path.Combine(
            parent.FullName,
            $".{directoryName}.hechao-rollback");
        var stagingOwnerPath = stagingDirectory + ".owner.json";
        var rollbackOwnerPath = rollbackDirectory + ".owner.json";
        var owner = new DeploymentDirectoryOwner(
            OwnerSchemaVersion,
            deployment.ImportId,
            deployment.ArchiveSha256,
            deployment.PreserveWorldData);

        try
        {
            using (await directoryAccessGate.EnterAsync(cancellationToken))
            {
                RecoverInterruptedSwitch(
                    serverDirectory,
                    stagingDirectory,
                    rollbackDirectory,
                    stagingOwnerPath,
                    rollbackOwnerPath);
                if (!Directory.Exists(serverDirectory) ||
                    IsReparsePoint(serverDirectory))
                {
                    throw new InvalidDataException(
                        "The recovered server directory is missing or unsafe.");
                }
                if (TryReadDeploymentMarker(serverDirectory, out var active) &&
                    active.ImportId == deployment.ImportId &&
                    string.Equals(
                        active.ArchiveSha256,
                        deployment.ArchiveSha256,
                        StringComparison.Ordinal))
                {
                    return Succeeded(
                        "PACKAGE_ALREADY_DEPLOYED",
                        "该整合包已经部署，服务端保持停止。 ");
                }
            }

            await ValidateArchiveAsync(
                archivePath,
                deployment,
                cancellationToken);
            PrepareControlledStaging(
                stagingDirectory,
                stagingOwnerPath,
                owner);
            var extracted = await SafeZipExtractor.ExtractAsync(
                archivePath,
                stagingDirectory,
                new ModpackInspectionLimits
                {
                    MaximumEntries = Math.Min(200_000, deployment.FileCount + 1),
                    MaximumExpandedBytes = Math.Max(1024, deployment.ExpandedBytes),
                    MaximumEntryBytes = Math.Max(1024, deployment.ExpandedBytes),
                    MaximumCompressionRatio = 10_000,
                    MaximumPathLength = 400
                },
                cancellationToken);
            if (extracted.Count != deployment.FileCount ||
                extracted.Sum(file => file.Size) != deployment.ExpandedBytes)
            {
                throw new InvalidDataException(
                    "The extracted server package does not match its immutable inventory.");
            }

            var stagedProperties = GetContainedPath(
                stagingDirectory,
                configuration.PropertiesRelativePath);
            var stagedMemorySettings = GetContainedPath(
                stagingDirectory,
                configuration.MemorySettingsRelativePath);
            var stagedStartScript = GetContainedPath(
                stagingDirectory,
                configuration.StartScriptRelativePath);
            if (!File.Exists(stagedProperties))
            {
                throw new InvalidDataException(
                    "The server package does not contain server.properties.");
            }

            ValidateManagedStartScript(stagedStartScript);

            ServerPropertiesEditor.ApplyDeploymentBinding(
                stagedProperties,
                configuration.Port);
            JvmMemorySettingsEditor.Apply(
                stagedMemorySettings,
                backupRoot,
                configuration.ServerId,
                deployment.InitialMemoryMiB,
                deployment.MaximumMemoryMiB,
                configuration.MaximumAllowedMemoryMiB);
            await WriteJsonAtomicallyAsync(
                Path.Combine(stagingDirectory, DeploymentMarkerName),
                new DeploymentMarker(
                    OwnerSchemaVersion,
                    deployment.ImportId,
                    deployment.ProfileId,
                    deployment.Version,
                    deployment.ArchiveSha256,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            foreach (var relativePath in configuration.HostManagedRelativePaths)
            {
                var source = configuration.GetContainedDeploymentPath(relativePath);
                if (!PathExists(source))
                {
                    throw new InvalidDataException(
                        $"A required host-managed path is missing: {relativePath}");
                }

                EnsureTreeHasNoReparsePoints(source);
            }

            var preservedPaths = GetPreservedPaths(deployment.PreserveWorldData);
            foreach (var relativePath in preservedPaths)
            {
                var source = configuration.GetContainedDeploymentPath(relativePath);
                if (PathExists(source))
                {
                    EnsureTreeHasNoReparsePoints(source);
                }
            }

            if (await findProcessIdAsync(cancellationToken) is not null)
            {
                throw new InvalidOperationException(
                    "The server started while its package was being prepared.");
            }

            DeleteControlledRollback(rollbackDirectory, rollbackOwnerPath);
            using (await directoryAccessGate.EnterAsync(cancellationToken))
            {
                await WriteJsonAtomicallyAsync(
                    rollbackOwnerPath,
                    owner,
                    cancellationToken);
                var oldMoved = false;
                var newActivated = false;
                try
                {
                    TransientFileSystem.MoveDirectory(
                        serverDirectory,
                        rollbackDirectory);
                    oldMoved = true;
                    foreach (var relativePath in preservedPaths)
                    {
                        MovePreservedPath(
                            rollbackDirectory,
                            stagingDirectory,
                            relativePath);
                    }

                    TransientFileSystem.MoveDirectory(
                        stagingDirectory,
                        serverDirectory);
                    newActivated = true;
                    if (!TryReadDeploymentMarker(
                            serverDirectory,
                            out var marker) ||
                        marker.ImportId != deployment.ImportId ||
                        !string.Equals(
                            marker.ArchiveSha256,
                            deployment.ArchiveSha256,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The activated server directory failed marker verification.");
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                        InvalidDataException or InvalidOperationException or
                        NotSupportedException)
                {
                    var rollbackErrors = RestoreAfterFailedSwitch(
                        serverDirectory,
                        stagingDirectory,
                        rollbackDirectory,
                        rollbackOwnerPath,
                        preservedPaths,
                        oldMoved,
                        newActivated);
                    if (rollbackErrors.Count == 0)
                    {
                        TryDeleteControlledStaging(
                            stagingDirectory,
                            stagingOwnerPath,
                            owner);
                    }

                    return Failed(
                        rollbackErrors.Count == 0
                            ? "PACKAGE_SWITCH_FAILED"
                            : "PACKAGE_ROLLBACK_FAILED",
                        AgentLog.Sanitize(
                            rollbackErrors.Count == 0
                                ? $"服务端目录切换失败，旧版本已恢复：{exception.Message}"
                                : $"服务端目录切换失败，自动恢复不完整：{exception.Message}；" +
                                  string.Join("；", rollbackErrors),
                            1800));
                }
            }

            TryDeleteFile(stagingOwnerPath);
            return Succeeded(
                "PACKAGE_DEPLOYED_STOPPED",
                "服务端整合包已原子部署并保留一个回滚目录；服务端保持停止。 ");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                NotSupportedException or CryptographicException or
                JsonException or OverflowException or ArgumentOutOfRangeException)
        {
            TryDeleteControlledStaging(stagingDirectory, stagingOwnerPath, owner);
            return Failed(
                "PACKAGE_DEPLOY_FAILED",
                AgentLog.Sanitize(
                    $"服务端整合包部署失败，当前目录未切换：{exception.Message}",
                    1800));
        }
    }

    private bool IsValid(ServerPackageDeploymentRequest deployment) =>
        deployment.ImportId != Guid.Empty &&
        ConfigurationPatterns.ServerId().IsMatch(deployment.ProfileId) &&
        VersionPattern().IsMatch(deployment.Version) &&
        Sha256Pattern().IsMatch(deployment.ArchiveSha256) &&
        deployment.ArchiveBytes is >= 1 and <= 16L * 1024 * 1024 * 1024 &&
        deployment.ExpandedBytes is >= 1 and <= 100L * 1024 * 1024 * 1024 &&
        deployment.FileCount is >= 1 and <= 200_000 &&
        deployment.InitialMemoryMiB is >= 512 and <= 65536 &&
        deployment.MaximumMemoryMiB is >= 512 and <= 65536 &&
        deployment.InitialMemoryMiB % 256 == 0 &&
        deployment.MaximumMemoryMiB % 256 == 0 &&
        deployment.InitialMemoryMiB <= deployment.MaximumMemoryMiB &&
        deployment.MaximumMemoryMiB <= configuration.MaximumAllowedMemoryMiB;

    private IReadOnlyList<string> GetPreservedPaths(bool preserveWorldData) =>
        [.. configuration.HostManagedRelativePaths
            .Concat(preserveWorldData
                ? configuration.WorldDataRelativePaths
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    private void RecoverInterruptedSwitch(
        string serverDirectory,
        string stagingDirectory,
        string rollbackDirectory,
        string stagingOwnerPath,
        string rollbackOwnerPath)
    {
        if (Directory.Exists(serverDirectory))
        {
            if (!Directory.Exists(rollbackDirectory) &&
                File.Exists(rollbackOwnerPath))
            {
                EnsureValidOwnerFile(rollbackOwnerPath);
                File.Delete(rollbackOwnerPath);
            }

            return;
        }

        if (!Directory.Exists(rollbackDirectory))
        {
            throw new InvalidDataException(
                "The server directory is missing and no controlled rollback exists.");
        }

        var rollbackOwner = ReadOwner(rollbackOwnerPath);
        EnsureTreeHasNoReparsePoints(rollbackDirectory);
        if (Directory.Exists(stagingDirectory))
        {
            var stagingOwner = ReadOwner(stagingOwnerPath);
            if (stagingOwner.ImportId != rollbackOwner.ImportId ||
                !string.Equals(
                    stagingOwner.ArchiveSha256,
                    rollbackOwner.ArchiveSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Interrupted deployment ownership markers do not match.");
            }

            EnsureTreeHasNoReparsePoints(stagingDirectory);
            foreach (var relativePath in GetPreservedPaths(
                         rollbackOwner.PreserveWorldData))
            {
                RestorePreservedPath(
                    stagingDirectory,
                    rollbackDirectory,
                    relativePath);
            }
        }

        TransientFileSystem.MoveDirectory(
            rollbackDirectory,
            serverDirectory);
        TryDeleteFile(rollbackOwnerPath);
        TryDeleteControlledStaging(
            stagingDirectory,
            stagingOwnerPath,
            rollbackOwner);
    }

    private static List<string> RestoreAfterFailedSwitch(
        string serverDirectory,
        string stagingDirectory,
        string rollbackDirectory,
        string rollbackOwnerPath,
        IReadOnlyList<string> preservedPaths,
        bool oldMoved,
        bool newActivated)
    {
        var errors = new List<string>();
        if (!oldMoved)
        {
            TryDeleteFile(rollbackOwnerPath);
            return errors;
        }

        try
        {
            if (newActivated && Directory.Exists(serverDirectory))
            {
                TransientFileSystem.MoveDirectory(
                    serverDirectory,
                    stagingDirectory);
            }

            foreach (var relativePath in preservedPaths.Reverse())
            {
                RestorePreservedPath(
                    stagingDirectory,
                    rollbackDirectory,
                    relativePath);
            }

            if (!Directory.Exists(serverDirectory) &&
                Directory.Exists(rollbackDirectory))
            {
                TransientFileSystem.MoveDirectory(
                    rollbackDirectory,
                    serverDirectory);
            }

            TryDeleteFile(rollbackOwnerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NotSupportedException)
        {
            errors.Add(exception.Message);
        }

        return errors;
    }

    private static void PrepareControlledStaging(
        string stagingDirectory,
        string ownerPath,
        DeploymentDirectoryOwner owner)
    {
        if (Directory.Exists(stagingDirectory) || File.Exists(ownerPath))
        {
            var existingOwner = ReadOwner(ownerPath);
            if (existingOwner.ImportId != owner.ImportId ||
                !string.Equals(
                    existingOwner.ArchiveSha256,
                    owner.ArchiveSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package staging directory belongs to another deployment.");
            }

            if (Directory.Exists(stagingDirectory))
            {
                EnsureTreeHasNoReparsePoints(stagingDirectory);
                Directory.Delete(stagingDirectory, recursive: true);
            }

            File.Delete(ownerPath);
        }

        WriteJsonAtomically(ownerPath, owner);
    }

    private static void DeleteControlledRollback(
        string rollbackDirectory,
        string ownerPath)
    {
        if (!Directory.Exists(rollbackDirectory) && !File.Exists(ownerPath))
        {
            return;
        }

        _ = ReadOwner(ownerPath);
        if (Directory.Exists(rollbackDirectory))
        {
            EnsureTreeHasNoReparsePoints(rollbackDirectory);
            Directory.Delete(rollbackDirectory, recursive: true);
        }

        File.Delete(ownerPath);
    }

    private static void TryDeleteControlledStaging(
        string stagingDirectory,
        string ownerPath,
        DeploymentDirectoryOwner expectedOwner)
    {
        try
        {
            if (!File.Exists(ownerPath))
            {
                return;
            }

            var owner = ReadOwner(ownerPath);
            if (owner.ImportId != expectedOwner.ImportId ||
                !string.Equals(
                    owner.ArchiveSha256,
                    expectedOwner.ArchiveSha256,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (Directory.Exists(stagingDirectory))
            {
                EnsureTreeHasNoReparsePoints(stagingDirectory);
                Directory.Delete(stagingDirectory, recursive: true);
            }

            File.Delete(ownerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException)
        {
        }
    }

    private static void RestorePreservedPath(
        string sourceRoot,
        string destinationRoot,
        string relativePath)
    {
        var destination = GetContainedPath(destinationRoot, relativePath);
        if (PathExists(destination))
        {
            return;
        }

        var source = GetContainedPath(sourceRoot, relativePath);
        if (!PathExists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(source))
        {
            TransientFileSystem.MoveDirectory(source, destination);
        }
        else
        {
            TransientFileSystem.MoveFile(source, destination);
        }
    }

    private static void MovePreservedPath(
        string sourceRoot,
        string destinationRoot,
        string relativePath)
    {
        var source = GetContainedPath(sourceRoot, relativePath);
        if (!PathExists(source))
        {
            return;
        }

        var destination = GetContainedPath(destinationRoot, relativePath);
        DeletePath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(source))
        {
            TransientFileSystem.MoveDirectory(source, destination);
        }
        else
        {
            TransientFileSystem.MoveFile(source, destination);
        }
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task ValidateArchiveAsync(
        string archivePath,
        ServerPackageDeploymentRequest deployment,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.GetFullPath(archivePath));
        if (!file.Exists || file.Length != deployment.ArchiveBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The cached server package size or file type is invalid.");
        }

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            256 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(digest),
                System.Text.Encoding.ASCII.GetBytes(
                    deployment.ArchiveSha256)))
        {
            throw new InvalidDataException(
                "The cached server package SHA-256 is invalid.");
        }
    }

    private static void ValidateManagedStartScript(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 1024 * 1024 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(
                file.Extension,
                ".bat",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The server package does not contain the managed start script.");
        }

        var text = System.Text.Encoding.Latin1.GetString(
            File.ReadAllBytes(file.FullName));
        if (!ManagedStartGuardPattern().IsMatch(text))
        {
            throw new InvalidDataException(
                "The server package start script is not managed-start aware.");
        }
    }

    private static void EnsureTreeHasNoReparsePoints(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidDataException(
                $"Preserved data contains a reparse point: {path}");
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (IsReparsePoint(entry))
                {
                    throw new InvalidDataException(
                        $"Preserved data contains a reparse point: {entry}");
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string GetContainedPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A deployment path escapes its controlled directory.");
        }

        return path;
    }

    private static bool TryReadDeploymentMarker(
        string serverDirectory,
        out DeploymentMarker marker)
    {
        marker = default!;
        var path = Path.Combine(serverDirectory, DeploymentMarkerName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > 64 * 1024 ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            marker = JsonSerializer.Deserialize<DeploymentMarker>(
                File.ReadAllText(path),
                JsonOptions)!;
            return marker is not null &&
                   marker.SchemaVersion == OwnerSchemaVersion &&
                   marker.ImportId != Guid.Empty &&
                   Sha256Pattern().IsMatch(marker.ArchiveSha256);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException)
        {
            return false;
        }
    }

    private static DeploymentDirectoryOwner ReadOwner(string path)
    {
        EnsureValidOwnerFile(path);
        var owner = JsonSerializer.Deserialize<DeploymentDirectoryOwner>(
            File.ReadAllText(path),
            JsonOptions);
        if (owner is null ||
            owner.SchemaVersion != OwnerSchemaVersion ||
            owner.ImportId == Guid.Empty ||
            !Sha256Pattern().IsMatch(owner.ArchiveSha256))
        {
            throw new InvalidDataException(
                "A deployment directory ownership marker is invalid.");
        }

        return owner;
    }

    private static void EnsureValidOwnerFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 64 * 1024 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "A deployment directory is missing its ownership marker.");
        }
    }

    private static void WriteJsonAtomically(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(
                temporary,
                JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task WriteJsonAtomicallyAsync(
        string path,
        object value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
                cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AgentCommandResult Succeeded(string code, string message) =>
        new(ServerControlCommandOutcome.Succeeded, code, message.Trim());

    private static AgentCommandResult Failed(string code, string message) =>
        new(ServerControlCommandOutcome.Failed, code, message.Trim());

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(
        "^[ \\t]*if not defined HECHAO_MANAGED_START pause[ \\t]*\\r?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
        RegexOptions.Multiline)]
    private static partial Regex ManagedStartGuardPattern();

    private sealed record DeploymentDirectoryOwner(
        int SchemaVersion,
        Guid ImportId,
        string ArchiveSha256,
        bool PreserveWorldData);

    private sealed record DeploymentMarker(
        int SchemaVersion,
        Guid ImportId,
        string ProfileId,
        string Version,
        string ArchiveSha256,
        DateTimeOffset DeployedAt);
}
