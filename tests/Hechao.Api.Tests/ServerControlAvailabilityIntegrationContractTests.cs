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
