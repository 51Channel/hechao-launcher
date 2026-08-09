using Hechao.Api.ServerControl;
using Microsoft.Extensions.Options;

namespace Hechao.Api.PackageImports;

internal sealed class PackageImportOrchestrationService(
    PackageImportOrchestrationRepository repository,
    IOptions<PackageImportOptions> packageOptions,
    TimeProvider timeProvider,
    ILogger<PackageImportOrchestrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!packageOptions.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                var queued = await repository.TryQueueDeploymentAsync(
                    now,
                    stoppingToken);
                var reconciled = await repository.ReconcileDeploymentAsync(
                    now,
                    stoppingToken);
                var finalized = await repository.FinalizeAsync(
                    now,
                    stoppingToken);
                if (queued != PackageImportOrchestrationOutcome.Progressed &&
                    reconciled != PackageImportOrchestrationOutcome.Progressed &&
                    finalized != PackageImportOrchestrationOutcome.Progressed)
                {
                    await Task.Delay(
                        queued == PackageImportOrchestrationOutcome.Waiting
                            ? TimeSpan.FromSeconds(5)
                            : TimeSpan.FromSeconds(2),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Package import deployment orchestration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
