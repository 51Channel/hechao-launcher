using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Hechao.Api.PackageImports;

public sealed class PackageImportAnalysisService(
    PackageImportRepository repository,
    PackageImportStorage storage,
    IOptions<PackageImportOptions> options,
    TimeProvider timeProvider,
    ILogger<PackageImportAnalysisService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var importId = await repository.ClaimAnalysisAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken);
                if (importId is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await AnalyzeAsync(importId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Package import analysis loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task AnalyzeAsync(
        Guid importId,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await storage.AnalyzeAsync(importId, cancellationToken);
            await repository.CompleteAnalysisAsync(
                importId,
                analysis,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(
                exception,
                "Package import {ImportId} was rejected during analysis.",
                importId);
            await repository.FailAsync(
                importId,
                "ARCHIVE_REJECTED",
                LimitMessage(exception.Message),
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                CryptographicException or OverflowException)
        {
            logger.LogError(
                exception,
                "Package import {ImportId} could not be analyzed.",
                importId);
            await repository.FailAsync(
                importId,
                "ANALYSIS_FAILED",
                "服务器无法完成整合包识别，原正式版本未发生变化。",
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
    }

    private static string LimitMessage(string value) =>
        value.Length <= 1000 ? value : value[..1000];
}
