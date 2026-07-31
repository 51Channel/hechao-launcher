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

        Assert.Equal(3, progressBars.Length);
        Assert.All(
            progressBars,
            progressBar => Assert.Contains(
                "Mode=OneWay",
                progressBar.Attribute("Value")?.Value));
    }

    [Fact]
    public void LauncherUpdateProgressBinding_IsExplicitlyOneWay()
    {
        var document = LoadLauncherXaml();
        XNamespace controls = "clr-namespace:Hechao.Launcher.Controls";

        var value = document
            .Descendants(controls + "AnimatedProgressBar")
            .Select(element => element.Attribute("Value")?.Value)
            .Single(binding =>
                binding?.Contains(
                    "LauncherUpdateProgress",
                    StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", value);
    }

    [Fact]
    public void LauncherUpdateDialog_UsesStableRowsAndExplicitPrimaryForeground()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var dialog = document
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "LauncherUpdateDialog");
        var progressRegion = dialog
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "LauncherUpdateProgressRegion");
        var progressBar = progressRegion
            .Elements()
            .Single(element => element.Name.LocalName == "AnimatedProgressBar");
        var status = progressRegion
            .Elements(presentation + "TextBlock")
            .Single(element =>
                (element.Attribute("Text")?.Value ?? string.Empty).Contains(
                    "LauncherUpdateStatus",
                    StringComparison.Ordinal));
        var installButton = dialog
            .Descendants(presentation + "Button")
            .Single(element =>
                (element.Attribute("Command")?.Value ?? string.Empty).Contains(
                    "InstallLauncherUpdateCommand",
                    StringComparison.Ordinal));

        Assert.Equal("560", dialog.Attribute("Width")?.Value);
        Assert.Equal("0", progressBar.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", status.Attribute("Grid.Row")?.Value);
        Assert.Equal("White", installButton.Attribute("Foreground")?.Value);
        Assert.All(
            installButton.Descendants().Where(
                element => element.Name.LocalName is "IconParkIcon" or "TextBlock"),
            element => Assert.Equal("White", element.Attribute("Foreground")?.Value));
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
    public void ClientProfileActions_AreHostedInReadinessPanel()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var readinessPanel = document.Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "ClientReadinessPanel");
        var actionsPanel = document.Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "ClientProfileActionsPanel");

        Assert.Contains(readinessPanel, actionsPanel.Ancestors());
        Assert.Equal("5", actionsPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal(4, actionsPanel.Elements(presentation + "Button").Count());
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
        Assert.Contains(
            controlStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value ==
                "HorizontalContentAlignment" &&
                setter.Attribute("Value")?.Value == "Stretch");
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

    [Fact]
    public void DiagnosticUpload_IsSeparateFromLocalBundleCreation()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var uploadButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Click")?.Value ==
                "UploadDiagnosticButton_OnClick");
        var createButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value ==
                "{Binding CreateDiagnosticBundleCommand}");

        Assert.Equal(
            "{Binding CanUploadDiagnosticBundle}",
            uploadButton.Attribute("IsEnabled")?.Value);
        Assert.Null(uploadButton.Attribute("Command"));
        Assert.Null(createButton.Attribute("Click"));
    }

    [Fact]
    public void SidebarAccountAvatar_UsesMinecraftSkinHeadAndHatLayers()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var brushes = document
            .Descendants(presentation + "ImageBrush")
            .Where(element =>
                element.Attribute("ImageSource")?.Value ==
                "{Binding AccountSkinSource}")
            .ToArray();

        Assert.Equal(2, brushes.Length);
        Assert.All(
            brushes,
            brush => Assert.Equal(
                "Absolute",
                brush.Attribute("ViewboxUnits")?.Value));
        Assert.Contains(
            brushes,
            brush => brush.Attribute("Viewbox")?.Value == "8,8,8,8");
        Assert.Contains(
            brushes,
            brush => brush.Attribute("Viewbox")?.Value == "40,8,8,8");
    }

    [Fact]
    public void ServerDetails_ProvidesConfirmedClientRemovalAction()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var button = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Click")?.Value == "DeleteProfileButton_OnClick");

        Assert.Equal(
            "{Binding CanDeleteSelectedProfile}",
            button.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding DeleteProfileToolTip}",
            button.Attribute("ToolTip")?.Value);
        Assert.Contains(
            button.Descendants(),
            element => element.Attribute("Kind")?.Value == "Delete");
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
