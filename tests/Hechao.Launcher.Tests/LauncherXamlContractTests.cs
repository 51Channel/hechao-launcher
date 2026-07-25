using System.Xml.Linq;

namespace Hechao.Launcher.Tests;

public sealed class LauncherXamlContractTests
{
    [Fact]
    public void ActiveDownloadPercentBinding_IsExplicitlyOneWay()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "MainWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var value = document
            .Descendants(presentation + "ProgressBar")
            .Select(element => element.Attribute("Value")?.Value)
            .Single(binding =>
                binding?.Contains(
                    "ActiveDownload.Percent",
                    StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", value);
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

        throw new DirectoryNotFoundException("The launcher repository root was not found.");
    }
}
