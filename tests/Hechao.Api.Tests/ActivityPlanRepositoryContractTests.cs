namespace Hechao.Api.Tests;

public sealed class ActivityPlanRepositoryContractTests
{
    [Fact]
    public void UnboundPlansStayOutsideThePlayerServerCatalogUntilBinding()
    {
        var source = ReadRepositorySource();

        Assert.Contains(
            "INSERT INTO launcher.unbound_activity_plans",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DELETE FROM launcher.unbound_activity_plans",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO launcher.servers",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "JOIN launcher.package_imports AS package",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LEFT JOIN launcher.package_imports AS package",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnboundPlansHaveExplicitPublishAndDeployGuards()
    {
        var source = ReadRepositorySource();

        Assert.True(
            source.Split(
                "ActivityPlanMutationStatus.PackageBindingRequired",
                StringSplitOptions.None).Length - 1 >= 4);
        Assert.Contains("if (state.IsUnbound)", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
    }

    private static string ReadRepositorySource()
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "ActivityPlans",
            "ActivityPlanRepository.cs"));
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

        throw new DirectoryNotFoundException("Repository not found.");
    }
}
