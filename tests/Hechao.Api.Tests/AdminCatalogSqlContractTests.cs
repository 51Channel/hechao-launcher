namespace Hechao.Api.Tests;

public sealed class AdminCatalogSqlContractTests
{
    [Fact]
    public void ControlTargetJoinsUseQualifiedServerProjectionColumns()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "Admin",
            "AdminCatalogRepository.cs"));

        var joinedServerQueryCount = source.Split(
            "FROM launcher.servers server",
            StringSplitOptions.None).Length - 1;
        var qualifiedProjectionCount = source.Split(
            "SELECT server.id, server.display_name",
            StringSplitOptions.None).Length - 1;

        Assert.Equal(2, joinedServerQueryCount);
        Assert.Equal(joinedServerQueryCount, qualifiedProjectionCount);
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
