namespace Hechao.Api.Tests;

public sealed class ActivityPlanEndpointContractTests
{
    [Fact]
    public void ProgramMapsAdminAndLoopbackWebsiteActivityPlans()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hechao.Api",
            "Program.cs"));
        var endpoints = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hechao.Api",
            "ActivityPlans",
            "ActivityPlanEndpoints.cs"));

        Assert.Contains("adminApi.MapAdminActivityPlans();", program);
        Assert.Contains("app.MapWebsiteActivityPlans();", program);
        Assert.Contains("/v1/internal/website/activity-plans", endpoints);
        Assert.Contains("IPAddress.IsLoopback", endpoints);
        Assert.Contains("X-Hechao-Website-Activity-Token", endpoints);
        Assert.Contains("/{planId}/deploy", endpoints);
        Assert.Contains("package_binding_required", endpoints);
        Assert.Contains("PackageBindingRequired", endpoints);
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
