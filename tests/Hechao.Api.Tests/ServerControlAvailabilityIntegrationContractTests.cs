namespace Hechao.Api.Tests;

public sealed class ServerControlAvailabilityIntegrationContractTests
{
    [Theory]
    [InlineData("Catalog", "CatalogRepository.cs")]
    [InlineData("Admin", "AdminCatalogRepository.cs")]
    [InlineData("Admin", "AdminAccessRepository.cs")]
    [InlineData("Velocity", "VelocityAuthorizationRepository.cs")]
    public void RuntimeSensitiveRepositoriesUseIndependentControlTargets(
        string area,
        string fileName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            area,
            fileName));

        Assert.Contains(
            "launcher.server_control_targets",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ServerControlAvailabilityRules.Resolve",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Catalog", "CatalogRepository.cs")]
    [InlineData("Admin", "AdminAccessRepository.cs")]
    [InlineData("Velocity", "VelocityAuthorizationRepository.cs")]
    public void ActivityPlansResolveRuntimeAgainstTheirBoundControlTarget(
        string area,
        string fileName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            area,
            fileName));
        var compact = string.Concat(source.Where(character => !char.IsWhiteSpace(character)));

        Assert.Contains(
            "ONcontrol_target.server_id=COALESCE(server.activity_target_server_id,server.id)",
            compact,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WHENserver.activity_plan_statusISNOTNULLTHEN'activity'",
            compact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VelocityDeploymentSlotUsesTheBoundPhysicalTarget()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "Velocity",
            "VelocityAuthorizationRepository.cs"));
        var compact = string.Concat(source.Where(character => !char.IsWhiteSpace(character)));

        Assert.Contains(
            "ONdeployment_slot.server_id=COALESCE(server.activity_target_server_id,server.id)",
            compact,
            StringComparison.Ordinal);
        Assert.Contains(
            "ANDdeployment_slot.velocity_target=server.velocity_target",
            compact,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Catalog", "CatalogRepository.cs", 3)]
    [InlineData("Admin", "AdminAccessRepository.cs", 1)]
    [InlineData("Velocity", "VelocityAuthorizationRepository.cs", 1)]
    public void PublishedPlanProjectionSuppressesItsPhysicalTarget(
        string area,
        string fileName,
        int expectedGuardCount)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            area,
            fileName));

        var guardCount = source.Split(
            "published_plan.activity_target_server_id = server.id",
            StringSplitOptions.None).Length - 1;
        Assert.Equal(expectedGuardCount, guardCount);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
