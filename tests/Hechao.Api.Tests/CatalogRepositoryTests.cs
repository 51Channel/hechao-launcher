using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class CatalogRepositoryTests
{
    [Theory]
    [InlineData("activity", false, ServerCatalogSection.Activity)]
    [InlineData("minigame-commercial-street", true, ServerCatalogSection.Activity)]
    [InlineData("survival2", false, ServerCatalogSection.Permanent)]
    [InlineData("Activity", false, ServerCatalogSection.Permanent)]
    public void ResolveCatalogSectionRecognizesLogicalActivityPlans(
        string velocityTarget,
        bool isActivityPlan,
        ServerCatalogSection expected)
    {
        Assert.Equal(
            expected,
            CatalogRepository.ResolveCatalogSection(velocityTarget, isActivityPlan));
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
        Assert.Contains(
            "published_plan.activity_target_server_id = server.id",
            CatalogRepository.AccessibleProfileSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogOmitsOnlyServersWithoutAnAvailableClientRelease()
    {
        var servers = new[]
        {
            CreateServer("unpublished", "unpublished-profile"),
            CreateServer("ready", "ready-profile")
        };
        var profiles = new[]
        {
            new ClientProfileSummary(
                "ready-profile",
                "Ready profile",
                "1.0.0",
                1,
                new string('a', 64),
                DateTimeOffset.UtcNow)
        };

        var filtered = CatalogRepository.FilterServersWithAvailableProfiles(servers, profiles);

        Assert.Equal("ready", Assert.Single(filtered).Id);
    }

    private static ServerSummary CreateServer(string id, string profileId) =>
        new(
            id,
            id,
            id,
            id[..1],
            ServerStatus.Online,
            0,
            20,
            "1.21.11",
            ModLoaderKind.Paper,
            AccessTier.Member,
            profileId);
}
