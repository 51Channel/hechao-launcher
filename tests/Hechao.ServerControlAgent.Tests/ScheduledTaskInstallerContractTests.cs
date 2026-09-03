namespace Hechao.ServerControlAgent.Tests;

public sealed class ScheduledTaskInstallerContractTests
{
    [Fact]
    public void LaunchTaskInstaller_UsesUnattendedS4ULogon()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "deploy",
            "windows",
            "server-control",
            "Install-MinecraftServerLaunchTask.ps1"));

        Assert.Contains("-LogonType S4U", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "-LogonType Interactive",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "installed.Principal.LogonType -ne 'S4U'",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleBridgeInstaller_UsesUnattendedS4ULogon()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "deploy",
            "windows",
            "server-control",
            "Install-MinecraftConsoleBridge.ps1"));

        Assert.Contains("-LogonType S4U", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "-LogonType Interactive",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "installed.Principal.LogonType -ne 'S4U'",
            script,
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
