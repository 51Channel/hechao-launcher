namespace Hechao.Api.Tests;

public sealed class ApiLoggingContractTests
{
    [Fact]
    public void ProductionApi_SuppressesFrameworkRequestInformationLogs()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Program.cs"));

        Assert.Contains(
            "builder.Logging.AddFilter(\"Microsoft.AspNetCore\", LogLevel.Warning);",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void JournaldPolicy_PreservesPublisherWorkingSpace()
    {
        var policy = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "deploy",
            "linux",
            "journald",
            "90-hechao-storage.conf"));

        Assert.Contains("SystemMaxUse=1G", policy, StringComparison.Ordinal);
        Assert.Contains("SystemKeepFree=8G", policy, StringComparison.Ordinal);
        Assert.Contains("MaxRetentionSec=14day", policy, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("The repository root could not be located.");
    }
}
