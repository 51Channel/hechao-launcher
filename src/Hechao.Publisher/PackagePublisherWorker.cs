using System.Reflection;
using Hechao.Contracts;
using Hechao.Modpack;

internal sealed class PackagePublisherWorker(
    PackagePublisherAgentConfiguration configuration,
    PackagePublisherApiClient apiClient)
{
    private readonly string agentVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    private readonly object activeJobLock = new();
    private Guid? activeImportId;

    internal Task RunAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(
            RunHeartbeatLoopAsync(cancellationToken),
            RunJobLoopAsync(cancellationToken));

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await apiClient.SendHeartbeatAsync(
                    agentVersion,
                    GetActiveImportId(),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                WriteStatus("heartbeat_failed", exception.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunJobLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var claim = await apiClient.ClaimAsync(cancellationToken);
                if (claim.Job is null)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(configuration.PollSeconds),
                        cancellationToken);
                    continue;
                }

                SetActiveImportId(claim.Job.ImportId);
                try
                {
                    var progress = new PackagePublisherProgressReporter(
                        apiClient,
                        claim.Job,
                        configuration.AgentId,
                        WriteStatus);
                    await WaitForWorkingSpaceAsync(
                        claim.Job,
                        progress,
                        cancellationToken);
                    await ProcessJobAsync(claim.Job, progress, cancellationToken);
                }
                finally
                {
                    ClearActiveImportId(claim.Job.ImportId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                WriteStatus("job_loop_failed", exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task WaitForWorkingSpaceAsync(
        PackagePublisherJobDelivery job,
        PackagePublisherProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var nextReportAt = DateTimeOffset.MinValue;
        while (true)
        {
            var snapshot = PackagePublisherWorkingSpace.Inspect(
                configuration.StateDirectory,
                job,
                configuration.MinimumFreeBytes,
                configuration.WorkingSpaceExpansionMultiplier);
            if (snapshot.AvailableBytes >= snapshot.RequiredBytes)
            {
                return;
            }

            await progress.ReportAsync(
                PackagePublisherProgressPhase.WaitingForWorkingSpace,
                0,
                0,
                Math.Min(snapshot.AvailableBytes, snapshot.RequiredBytes),
                snapshot.RequiredBytes,
                force: nextReportAt == DateTimeOffset.MinValue,
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            if (now >= nextReportAt)
            {
                WriteStatus(
                    "working_space_wait",
                    $"import={job.ImportId:D} required_bytes={snapshot.RequiredBytes} " +
                    $"available_bytes={snapshot.AvailableBytes}");
                nextReportAt = now.AddMinutes(5);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private async Task ProcessJobAsync(
        PackagePublisherJobDelivery job,
        PackagePublisherProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var jobRoot = GetJobRoot(job.ImportId);
        var archivePath = Path.Combine(jobRoot, "client.zip");
        var sourceDirectory = Path.Combine(jobRoot, "source");
        var distributionDirectory = Path.Combine(jobRoot, "distribution");
        try
        {
            Directory.CreateDirectory(jobRoot);
            await progress.ReportAsync(
                PackagePublisherProgressPhase.DownloadingArchive,
                0,
                0,
                0,
                job.ClientArchiveBytes,
                force: true,
                cancellationToken);
            await apiClient.DownloadClientArchiveAsync(
                job,
                archivePath,
                (processed, total, token) => progress.ReportAsync(
                    PackagePublisherProgressPhase.DownloadingArchive,
                    0,
                    0,
                    processed,
                    total,
                    force: processed == total,
                    token),
                cancellationToken);
            DeleteGeneratedDirectory(sourceDirectory);
            DeleteGeneratedDirectory(distributionDirectory);
            await progress.ReportAsync(
                PackagePublisherProgressPhase.ExtractingArchive,
                0,
                0,
                0,
                0,
                force: true,
                cancellationToken);
            await SafeZipExtractor.ExtractAsync(
                archivePath,
                sourceDirectory,
                new ModpackInspectionLimits(),
                cancellationToken);
            await progress.ReportAsync(
                PackagePublisherProgressPhase.BuildingDistribution,
                0,
                0,
                0,
                0,
                force: true,
                cancellationToken);
            var signingKey = new SigningKeyInput(
                configuration.SigningKeyPath,
                configuration.SigningKeyEntropyLabel,
                configuration.SigningKeyBlobSha256?.ToUpperInvariant());
            var objectBaseUri = configuration.GetProfileObjectBaseUri(job.ProfileId);
            var build = await ClientDistributionBuilder.BuildAsync(
                new ClientDistributionBuildOptions(
                    sourceDirectory,
                    distributionDirectory,
                    job.ProfileId,
                    job.Version,
                    job.MinecraftVersion,
                    job.JavaMajorVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    job.Loader,
                    job.LoaderVersion,
                    configuration.SigningKeyId,
                    signingKey,
                    objectBaseUri,
                    DateTimeOffset.UtcNow,
                    []),
                cancellationToken);
            var upload = await new OssDistributionUploader(
                new OssUploadOptions(
                    distributionDirectory,
                    configuration.OssBucket,
                    configuration.OssRegion,
                    configuration.OssEndpoint,
                    configuration.OssObjectPrefix,
                    configuration.OssCredentialPath,
                    configuration.OssCredentialEntropyLabel,
                    configuration.Parallelism))
                .UploadAsync(
                    (uploadProgress, token) => progress.ReportAsync(
                        PackagePublisherProgressPhase.PublishingObjects,
                        uploadProgress.CompletedObjects,
                        uploadProgress.TotalObjects,
                        uploadProgress.ProcessedBytes,
                        uploadProgress.TotalBytes,
                        force: uploadProgress.CompletedObjects == 0 ||
                               uploadProgress.CompletedObjects ==
                               uploadProgress.TotalObjects,
                        token),
                    cancellationToken);
            var envelope = await File.ReadAllBytesAsync(
                build.ManifestPath,
                cancellationToken);
            await progress.ReportAsync(
                PackagePublisherProgressPhase.Finalizing,
                0,
                0,
                1,
                1,
                force: true,
                cancellationToken);
            await apiClient.CompleteAsync(
                job.ImportId,
                new PackagePublisherCompletionRequest(
                    configuration.AgentId,
                    job.AttemptCount,
                    PackagePublisherJobOutcome.Succeeded,
                    "PUBLISHED",
                    "客户端对象和签名清单已完成不可变校验。",
                    Convert.ToBase64String(envelope),
                    upload.Uploaded,
                    upload.AlreadyPresent,
                    upload.UploadedBytes),
                cancellationToken);
            TryDeleteGeneratedDirectory(jobRoot);
            WriteStatus("job_succeeded", job.ImportId.ToString("D"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteStatus("job_failed", exception.Message);
            try
            {
                await apiClient.CompleteAsync(
                    job.ImportId,
                    new PackagePublisherCompletionRequest(
                        configuration.AgentId,
                        job.AttemptCount,
                        PackagePublisherJobOutcome.Failed,
                        "PUBLISHER_JOB_FAILED",
                        "客户端发布失败，服务端部署未开始，正式档案未变化。",
                        null,
                        0,
                        0,
                        0),
                    cancellationToken);
            }
            catch (Exception completionException)
            {
                WriteStatus("job_failure_report_failed", completionException.Message);
            }
        }
    }

    private string GetJobRoot(Guid importId)
    {
        var root = Path.GetFullPath(configuration.StateDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (IsReparsePoint(root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)))
        {
            throw new InvalidDataException(
                "The package publisher state directory cannot be a reparse point.");
        }

        var path = Path.GetFullPath(Path.Combine(root, "jobs", importId.ToString("N")));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The package publisher job directory escapes its state directory.");
        }

        return path;
    }

    private Guid? GetActiveImportId()
    {
        lock (activeJobLock)
        {
            return activeImportId;
        }
    }

    private void SetActiveImportId(Guid importId)
    {
        lock (activeJobLock)
        {
            if (activeImportId is not null)
            {
                throw new InvalidOperationException(
                    "Only one package publisher job can be active.");
            }

            activeImportId = importId;
        }
    }

    private void ClearActiveImportId(Guid importId)
    {
        lock (activeJobLock)
        {
            if (activeImportId == importId)
            {
                activeImportId = null;
            }
        }
    }

    private void DeleteGeneratedDirectory(string path)
    {
        var stateRoot = Path.GetFullPath(configuration.StateDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(stateRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to delete a path outside the package publisher state directory.");
        }

        if (Directory.Exists(fullPath))
        {
            EnsureTreeHasNoReparsePoints(fullPath);
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private void TryDeleteGeneratedDirectory(string path)
    {
        try
        {
            DeleteGeneratedDirectory(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NotSupportedException)
        {
            WriteStatus("job_cleanup_failed", exception.Message);
        }
    }

    private static void EnsureTreeHasNoReparsePoints(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidDataException(
                "A package publisher job path is a reparse point.");
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
                        "A package publisher job contains a reparse point.");
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

    private static void WriteStatus(string code, string value)
    {
        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        if (sanitized.Length > 500)
        {
            sanitized = sanitized[..500];
        }

        Console.Error.WriteLine(
            $"{DateTimeOffset.UtcNow:O} {code} {sanitized}");
    }
}
