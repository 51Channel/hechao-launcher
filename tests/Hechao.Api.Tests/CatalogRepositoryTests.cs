using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class CatalogRepositoryTests
{
    [Theory]
    [InlineData("activity", ServerCatalogSection.Activity)]
    [InlineData("survival2", ServerCatalogSection.Permanent)]
    [InlineData("Activity", ServerCatalogSection.Permanent)]
    public void ResolveCatalogSectionMapsOnlyActivityTargetToActivity(
        string velocityTarget,
        ServerCatalogSection expected)
    {
        Assert.Equal(expected, CatalogRepository.ResolveCatalogSection(velocityTarget));
    }

    [Fact]
    public void ActivityPlanIsClosedUntilItsExactPackageIsDeployed()
    {
        var expected = Guid.NewGuid();

        Assert.Equal(
            ServerStatus.Closed,
            CatalogRepository.ResolveActivityDeploymentStatus(
                ServerStatus.Online,
                isActivityPlan: true,
                expected,
                deployedPackageImportId: null));
        Assert.Equal(
            ServerStatus.Closed,
            CatalogRepository.ResolveActivityDeploymentStatus(
                ServerStatus.Online,
                isActivityPlan: true,
                expected,
                Guid.NewGuid()));
        Assert.Equal(
            ServerStatus.Online,
            CatalogRepository.ResolveActivityDeploymentStatus(
                ServerStatus.Online,
                isActivityPlan: true,
                expected,
                expected));
        Assert.Equal(
            ServerStatus.Online,
            CatalogRepository.ResolveActivityDeploymentStatus(
                ServerStatus.Online,
                isActivityPlan: false,
                expectedPackageImportId: null,
                deployedPackageImportId: null));
    }
}
