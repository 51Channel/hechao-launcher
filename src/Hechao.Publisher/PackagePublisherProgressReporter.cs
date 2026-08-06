using System.Diagnostics;
using Hechao.Contracts;

internal sealed class PackagePublisherProgressReporter(
    PackagePublisherApiClient apiClient,
    PackagePublisherJobDelivery job,
    string agentId,
    Action<string, string> writeStatus)
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim reportGate = new(1, 1);
    private long lastReportTimestamp;
    private PackagePublisherProgressPhase? currentPhase;
    private int completedObjects;
    private long processedBytes;

    internal async Task ReportAsync(
        PackagePublisherProgressPhase phase,
        int completed,
        int total,
        long processed,
        long totalBytes,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && !IsDue())
        {
            return;
        }

        if (force)
        {
            await reportGate.WaitAsync(cancellationToken);
        }
        else if (!await reportGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (!force && !IsDue())
            {
                return;
            }

            if (currentPhase is not null && phase < currentPhase)
            {
                return;
            }

            if (phase == currentPhase)
            {
                completed = Math.Max(completedObjects, completed);
                processed = Math.Max(processedBytes, processed);
            }
            else
            {
                currentPhase = phase;
                completedObjects = 0;
                processedBytes = 0;
            }

            completedObjects = Math.Min(completed, total);
            processedBytes = Math.Min(processed, totalBytes);
            lastReportTimestamp = Stopwatch.GetTimestamp();
            await apiClient.ReportProgressAsync(
                job.ImportId,
                new PackagePublisherProgressRequest(
                    agentId,
                    job.AttemptCount,
                    phase,
                    completedObjects,
                    total,
                    processedBytes,
                    totalBytes),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            writeStatus("progress_report_failed", exception.Message);
        }
        finally
        {
            reportGate.Release();
        }
    }

    private bool IsDue() =>
        lastReportTimestamp == 0 ||
        Stopwatch.GetElapsedTime(lastReportTimestamp) >= MinimumInterval;
}
