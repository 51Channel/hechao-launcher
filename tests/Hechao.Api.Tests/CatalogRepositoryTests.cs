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

    [Theory]
    [InlineData(AccessTier.Member, AccessTier.Participant, null, false)]
    [InlineData(AccessTier.Participant, AccessTier.Participant, null, true)]
    [InlineData(AccessTier.Member, AccessTier.Participant, AdminServerAccessDecision.Allow, true)]
    [InlineData(AccessTier.Administrator, AccessTier.Member, AdminServerAccessDecision.Deny, false)]
    public void CanJoinServerHonorsTierAndExplicitOverrides(
        AccessTier accessTier,
        AccessTier minimumTier,
        AdminServerAccessDecision? overrideDecision,
        bool expected)
    {
        Assert.Equal(
            expected,
            CatalogRepository.CanJoinServer(accessTier, minimumTier, overrideDecision));
    }

    [Fact]
    public void AnonymousCatalogNeverGrantsJoinAccess()
    {
        Assert.False(CatalogRepository.CanJoinServer(
            accessTier: null,
            AccessTier.Member,
            overrideDecision: AdminServerAccessDecision.Allow));
    }

    [Theory]
    [InlineData(ServerCatalogSection.Activity, false, true)]
    [InlineData(ServerCatalogSection.Permanent, false, false)]
    [InlineData(ServerCatalogSection.Permanent, true, true)]
    public void AuthenticatedCatalogKeepsActivitiesButFiltersPermanentServers(
        ServerCatalogSection section,
        bool canJoin,
        bool expected)
    {
        Assert.Equal(
            expected,
            CatalogRepository.ShouldIncludeServer(
                isAuthenticated: true,
                section,
                canJoin));
    }

    [Fact]
    public void ActivityProfileDownloadRequiresVisiblePlayerActivityButNotJoinTier()
    {
        Assert.Contains(
            "server.velocity_target = 'activity'",
            CatalogRepository.AccessibleProfileSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "server.is_visible",
            CatalogRepository.AccessibleProfileSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "server.server_role = 'Player'",
            CatalogRepository.AccessibleProfileSql,
            StringComparison.Ordinal);
    }
}
