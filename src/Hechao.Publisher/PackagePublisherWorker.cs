using System.Reflection;
using Hechao.Contracts;
using Hechao.Modpack;

internal sealed class PackagePublisherWorker(
    PackagePublisherAgentConfiguration configuration,
    PackagePublisherApiClient apiClient)
{
    private readonly string agentVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

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
                await apiClient.SendHeartbeatAsync(agentVersion, cancellationToken);
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

                await ProcessJobAsync(claim.Job, cancellationToken);
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

    private async Task ProcessJobAsync(
        PackagePublisherJobDelivery job,
        CancellationToken cancellationToken)
    {
        var jobRoot = GetJobRoot(job.ImportId);
        var archivePath = Path.Combine(jobRoot, "client.zip");
        var sourceDirectory = Path.Combine(jobRoot, "source");
        var distributionDirectory = Path.Combine(jobRoot, "distribution");
        try
        {
            Directory.CreateDirectory(jobRoot);
            await apiClient.DownloadClientArchiveAsync(
                job,
                archivePath,
                cancellationToken);
            DeleteGeneratedDirectory(sourceDirectory);
            DeleteGeneratedDirectory(distributionDirectory);
            await SafeZipExtractor.ExtractAsync(
                archivePath,
                sourceDirectory,
                new ModpackInspectionLimits(),
                cancellationToken);
            var signingKey = new SigningKeyInput(
                configuration.SigningKeyPath,
                configuration.SigningKeyEntropyLabel,
                configuration.SigningKeyBlobSha256?.ToUpperInvariant());
            var objectBaseUri = new Uri(
                configuration.ApiBaseUrl.TrimEnd('/') +
                $"/v1/profiles/{job.ProfileId}/",
                UriKind.Absolute);
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
                .UploadAsync(cancellationToken);
            var envelope = await File.ReadAllBytesAsync(
                build.ManifestPath,
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
            DeleteGeneratedDirectory(jobRoot);
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
        var path = Path.GetFullPath(Path.Combine(root, "jobs", importId.ToString("N")));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The package publisher job directory escapes its state directory.");
        }

        return path;
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
            Directory.Delete(fullPath, recursive: true);
        }
    }

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
