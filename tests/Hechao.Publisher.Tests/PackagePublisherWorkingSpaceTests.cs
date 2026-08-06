using Hechao.Contracts;

namespace Hechao.Publisher.Tests;

public sealed class PackagePublisherWorkingSpaceTests
{
    [Fact]
    public void CalculateRequiredBytes_UsesConfiguredExpansionMultiplier()
    {
        var job = CreateJob(archiveBytes: 100);

        var required = PackagePublisherWorkingSpace.CalculateRequiredBytes(
            job,
            minimumFreeBytes: 1_000,
            expansionMultiplier: 5);

        Assert.Equal(2_100, required);
    }

    [Fact]
    public void CalculateRequiredBytes_UsesDefaultConservativeMultiplier()
    {
        var job = CreateJob(archiveBytes: 100);

        var required = PackagePublisherWorkingSpace.CalculateRequiredBytes(
            job,
            minimumFreeBytes: 1_000,
            expansionMultiplier: 4);

        Assert.Equal(1_900, required);
    }

    [Fact]
    public void CalculateRequiredBytes_RejectsInvalidArchiveSize()
    {
        var job = CreateJob(archiveBytes: 0);

        Assert.Throws<InvalidDataException>(() =>
            PackagePublisherWorkingSpace.CalculateRequiredBytes(job, 1_000, 4));
    }

    private static PackagePublisherJobDelivery CreateJob(long archiveBytes) =>
        new(
            Guid.NewGuid(),
            1,
            "profile-id",
            "1.0.0",
            "1.21.11",
            21,
            "NeoForge",
            "21.11.42-beta",
            archiveBytes,
            new string('a', 64));
}
