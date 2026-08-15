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
    [InlineData("Velocity", "VelocityAuthorizationRepository.cs")]
    public void DynamicDeploymentSlotsUseTheirOwnControlTargetBeforeActivityFallback(
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

        var slotJoin = source.IndexOf(
            "LEFT JOIN launcher.deployment_slots deployment_slot",
            StringComparison.Ordinal);
        var targetJoin = source.IndexOf(
            "LEFT JOIN launcher.server_control_targets control_target",
            slotJoin,
            StringComparison.Ordinal);
        var independentTarget = source.IndexOf(
            "WHEN deployment_slot.server_id IS NOT NULL THEN server.id",
            targetJoin,
            StringComparison.Ordinal);
        var sharedActivityFallback = source.IndexOf(
            "WHEN server.activity_plan_status IS NOT NULL THEN 'activity'",
            independentTarget,
            StringComparison.Ordinal);

        Assert.InRange(slotJoin, 0, targetJoin - 1);
        Assert.InRange(targetJoin, slotJoin + 1, independentTarget - 1);
        Assert.InRange(independentTarget, targetJoin + 1, sharedActivityFallback - 1);
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
