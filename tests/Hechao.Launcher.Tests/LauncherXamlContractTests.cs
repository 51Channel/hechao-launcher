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
        XNamespace controls =
            "clr-namespace:Hechao.Launcher.Controls";

        var value = document
            .Descendants(controls + "AnimatedProgressBar")
            .Select(element => element.Attribute("Value")?.Value)
            .Single(binding =>
                binding?.Contains(
                    "ActiveDownload.Percent",
                    StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", value);
    }

    [Fact]
    public void DownloadProgressBars_UseAnimatedControl()
    {
        var document = LoadLauncherXaml();
        XNamespace controls =
            "clr-namespace:Hechao.Launcher.Controls";

        var progressBars = document
            .Descendants(controls + "AnimatedProgressBar")
            .ToArray();

        Assert.Equal(2, progressBars.Length);
        Assert.All(
            progressBars,
            progressBar => Assert.Contains(
                "Mode=OneWay",
                progressBar.Attribute("Value")?.Value));
    }

    [Fact]
    public void ServerClientDirectoryRow_AutoSizesWithoutClipping()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var row = document
            .Descendants(presentation + "RowDefinition")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ClientDirectoryRow");

        Assert.Equal("Auto", row.Attribute("Height")?.Value);
    }

    [Fact]
    public void ServerDetails_HidesTheRedundantScrollbar()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollViewer = document
            .Descendants(presentation + "ScrollViewer")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ServerDetailsScrollViewer");

        Assert.Equal(
            "Hidden",
            scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void Theme_RemovesPageFocusOutlineAndUsesSlimScrollbar()
    {
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var scrollViewerStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value.Contains(
                    "ScrollViewer",
                    StringComparison.Ordinal) == true &&
                element.Attribute(XName.Get(
                    "Key",
                    "http://schemas.microsoft.com/winfx/2006/xaml")) is null);
        Assert.Contains(
            scrollViewerStyle.Descendants(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                setter.Attribute("Value")?.Value == "{x:Null}");

        var scrollBarStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value.Contains(
                    "ScrollBar",
                    StringComparison.Ordinal) == true &&
                element.Attribute(XName.Get(
                    "Key",
                    "http://schemas.microsoft.com/winfx/2006/xaml")) is null);
        Assert.Contains(
            scrollBarStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Width" &&
                setter.Attribute("Value")?.Value == "9");
    }

    [Fact]
    public void AccountTabs_UseEqualWidthItemsWithCompleteBorders()
    {
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var controlStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "AccountTabControlStyle");
        var itemsPanel = controlStyle
            .Descendants(presentation + "UniformGrid")
            .Single();
        var style = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value == "AccountTabStyle");
        var tabBorder = style
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "TabBackground");

        Assert.Equal("1", itemsPanel.Attribute("Rows")?.Value);
        Assert.Equal("True", tabBorder.Attribute("SnapsToDevicePixels")?.Value);
        Assert.Equal(
            "{TemplateBinding BorderThickness}",
            tabBorder.Attribute("BorderThickness")?.Value);
        Assert.Empty(style.Descendants(presentation + "Rectangle"));
        Assert.Contains(
            style.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Margin" &&
                setter.Attribute("Value")?.Value == "0,0,6,0");
        Assert.Contains(
            style.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "BorderThickness" &&
                setter.Attribute("Value")?.Value == "1");
        Assert.Contains(
            style.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "UseLayoutRounding" &&
                setter.Attribute("Value")?.Value == "True");
    }

    private static XDocument LoadLauncherXaml()
    {
        var repositoryRoot = FindRepositoryRoot();
        return XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "MainWindow.xaml"));
    }

    private static XDocument LoadThemeXaml()
    {
        var repositoryRoot = FindRepositoryRoot();
        return XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "Themes",
            "HechaoTheme.xaml"));
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
