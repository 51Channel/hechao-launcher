using Hechao.Contracts;

internal sealed record PackagePublisherWorkingSpaceSnapshot(
    long RequiredBytes,
    long AvailableBytes);

internal static class PackagePublisherWorkingSpace
{
    internal static long CalculateRequiredBytes(
        PackagePublisherJobDelivery job,
        long minimumFreeBytes,
        int expansionMultiplier)
    {
        if (job.ClientArchiveBytes <= 0 ||
            minimumFreeBytes < 0 ||
            expansionMultiplier < 1)
        {
            throw new InvalidDataException(
                "The package publisher working-space inputs are invalid.");
        }

        var expandedBytes = MultiplySaturating(
            job.ClientArchiveBytes,
            expansionMultiplier);
        return AddSaturating(
            minimumFreeBytes,
            AddSaturating(
                job.ClientArchiveBytes,
                MultiplySaturating(expandedBytes, 2)));
    }

    internal static PackagePublisherWorkingSpaceSnapshot Inspect(
        string stateDirectory,
        PackagePublisherJobDelivery job,
        long minimumFreeBytes,
        int expansionMultiplier)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(stateDirectory));
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException(
                "The package publisher state volume is invalid.");
        }

        var drive = new DriveInfo(root);
        return new PackagePublisherWorkingSpaceSnapshot(
            CalculateRequiredBytes(job, minimumFreeBytes, expansionMultiplier),
            drive.AvailableFreeSpace);
    }

    private static long AddSaturating(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long MultiplySaturating(long value, long multiplier) =>
        value > long.MaxValue / multiplier ? long.MaxValue : value * multiplier;
}
