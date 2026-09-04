using System.Xml.Linq;

namespace Hechao.Launcher.Tests;

public sealed class LauncherXamlContractTests
{
    [Fact]
    public void ButtonStyles_MatchTheControlTypeThatUsesThem()
    {
        var launcher = LoadLauncherXaml();
        var theme = LoadThemeXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styleTargets = theme
            .Descendants(presentation + "Style")
            .Where(style => style.Attribute(x + "Key") is not null)
            .ToDictionary(
                style => style.Attribute(x + "Key")!.Value,
                style => style.Attribute("TargetType")?.Value,
                StringComparer.Ordinal);

        var styledButtons = launcher
            .Descendants()
            .Where(element =>
                element.Name.Namespace == presentation &&
                element.Name.LocalName is "Button" or "ToggleButton")
            .Select(element => new
            {
                Element = element,
                StyleKey = GetStaticResourceKey(element.Attribute("Style")?.Value)
            })
            .Where(item => item.StyleKey is not null && styleTargets.ContainsKey(item.StyleKey));

        foreach (var item in styledButtons)
        {
            Assert.Equal(
                item.Element.Name.LocalName,
                styleTargets[item.StyleKey!]);
        }
    }

    [Fact]
    public void TopNavigation_PreservesFiveWorkspacesAndStableAutomationNames()
    {
        var launcher = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var titleBar = launcher
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "TitleBar");
        var primaryWorkspaces = new[]
        {
            (State: "IsServersPage", Command: "ShowServersCommand", Name: "服务器"),
            (State: "IsActivitiesPage", Command: "ShowActivitiesCommand", Name: "活动"),
            (State: "IsDownloadsPage", Command: "ShowDownloadsCommand", Name: "下载中心"),
        };

        foreach (var workspace in primaryWorkspaces)
        {
            var toggle = titleBar
                .Descendants(presentation + "ToggleButton")
                .Single(element =>
                    element.Attribute("IsChecked")?.Value.Contains(
                        workspace.State,
                        StringComparison.Ordinal) == true);

            Assert.Contains("Mode=TwoWay", toggle.Attribute("IsChecked")!.Value);
            Assert.Equal(
                $"{{Binding {workspace.Command}}}",
                toggle.Attribute("Command")?.Value);
            Assert.Equal(
                workspace.Name,
                toggle.Attribute("AutomationProperties.Name")?.Value);
        }

        var primaryNavigationNames = titleBar
            .Descendants(presentation + "ToggleButton")
            .Where(element => primaryWorkspaces.Any(workspace =>
                element.Attribute("IsChecked")?.Value.Contains(
                    workspace.State,
                    StringComparison.Ordinal) == true))
            .Select(element => element.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            primaryWorkspaces.Select(workspace => workspace.Name),
            primaryNavigationNames);

        foreach (var workspace in new[]
                 {
                     (Command: "ShowSettingsPageCommand", Name: "启动器设置"),
                     (Command: "ShowAccountCommand", Name: "赫朝账户"),
                 })
        {
            var button = titleBar
                .Descendants(presentation + "Button")
                .Single(element =>
                    element.Attribute("Command")?.Value ==
                    $"{{Binding {workspace.Command}}}");
            Assert.Equal(
                workspace.Name,
                button.Attribute("AutomationProperties.Name")?.Value);
        }
    }

    [Fact]
    public void MainWindow_OpensAtTheComfortableDefaultAndRemainsResizable()
    {
        var window = LoadLauncherXaml().Root!;

        Assert.Equal("1200", window.Attribute("Width")?.Value);
        Assert.Equal("720", window.Attribute("Height")?.Value);
        Assert.Equal("1060", window.Attribute("MinWidth")?.Value);
        Assert.Equal("640", window.Attribute("MinHeight")?.Value);
        Assert.Equal("CenterScreen", window.Attribute("WindowStartupLocation")?.Value);
        Assert.Equal("CanResize", window.Attribute("ResizeMode")?.Value);
    }

    [Fact]
    public void ServerDirectory_DefaultWindowFitsTwoCardsBeforeWrapping()
    {
        var launcher = LoadLauncherXaml();
        var theme = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var windowWidth = double.Parse(launcher.Root!.Attribute("Width")!.Value);
        var inspectorWidth = double.Parse(
            launcher
                .Descendants(presentation + "ColumnDefinition")
                .Single(element =>
                    element.Attribute(x + "Name")?.Value == "InspectorColumn")
                .Attribute("Width")!
                .Value);
        var serverList = launcher
            .Descendants(presentation + "ListBox")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value == "{Binding Servers}");
        var wrapPanel = serverList
            .Descendants(presentation + "WrapPanel")
            .Single();
        var listMargin = serverList
            .Attribute("Margin")!
            .Value
            .Split(',')
            .Select(double.Parse)
            .ToArray();
        var serverItemStyle = theme
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ServerCardListItemStyle");
        var cardWidth = double.Parse(
            serverItemStyle
                .Elements(presentation + "Setter")
                .Single(setter => setter.Attribute("Property")?.Value == "Width")
                .Attribute("Value")!
                .Value);
        var cardMargin = serverItemStyle
            .Elements(presentation + "Setter")
            .Single(setter => setter.Attribute("Property")?.Value == "Margin")
            .Attribute("Value")!
            .Value
            .Split(',')
            .Select(double.Parse)
            .ToArray();

        const double rootBorderWidth = 2;
        const double verticalScrollBarAllowance = 18;
        var availableCatalogWidth =
            windowWidth - inspectorWidth - rootBorderWidth -
            listMargin[0] - listMargin[2] - verticalScrollBarAllowance;
        var requiredTwoCardWidth =
            2 * (cardWidth + cardMargin[0] + cardMargin[2]);

        Assert.Equal("Horizontal", wrapPanel.Attribute("Orientation")?.Value);
        Assert.True(
            availableCatalogWidth >= requiredTwoCardWidth,
            $"Default catalog width {availableCatalogWidth} must fit two " +
            $"server cards requiring {requiredTwoCardWidth}.");
    }

    [Fact]
    public void ThemeToggle_UsesDarkDefaultAndAppliesBeforeWindowCreation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var launcher = LoadLauncherXaml();
        var app = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "App.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var toggle = launcher
            .Descendants(presentation + "ToggleButton")
            .Single(element => element.Attribute("IsChecked")?.Value.Contains(
                "UseDarkMode",
                StringComparison.Ordinal) == true);
        Assert.Equal("38", toggle.Attribute("Width")?.Value);
        Assert.Equal("38", toggle.Attribute("Height")?.Value);
        Assert.Equal("黑夜模式", toggle.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding ThemeToggleToolTip}",
            toggle.Attribute("ToolTip")?.Value);
        Assert.Null(toggle.Attribute("Command"));
        Assert.Equal(
            new[] { "Moon", "SunOne" },
            toggle
                .Descendants()
                .Where(element => element.Name.LocalName == "IconParkIcon")
                .Select(element => element.Attribute("Kind")?.Value));

        var mergedSources = app
            .Descendants(presentation + "ResourceDictionary.MergedDictionaries")
            .Elements(presentation + "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
            ["/Themes/DarkPalette.xaml", "/Themes/HechaoTheme.xaml"],
            mergedSources);

        var startupSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "App.xaml.cs"));
        var applyIndex = startupSource.IndexOf(
            "themeService.Apply(settings.UseDarkMode);",
            StringComparison.Ordinal);
        var windowIndex = startupSource.IndexOf(
            "new MainWindow(settingsStore, themeService, settings);",
            StringComparison.Ordinal);
        Assert.True(applyIndex >= 0, "The saved theme must be applied during startup.");
        Assert.True(
            windowIndex > applyIndex,
            "The theme must be applied before MainWindow is constructed.");
    }

    [Fact]
    public void ThemeBrushes_UseDynamicReferencesThroughoutTheLoadedInterface()
    {
        var launcher = LoadLauncherXaml();
        var theme = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var themeBrushKeys = theme
            .Root!
            .Elements(presentation + "SolidColorBrush")
            .Select(element => element.Attribute(x + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var attributes = launcher
            .Descendants()
            .Concat(theme.Descendants())
            .Attributes()
            .ToArray();

        Assert.NotEmpty(themeBrushKeys);
        Assert.DoesNotContain(
            attributes,
            attribute =>
            {
                var resourceKey = GetStaticResourceKey(attribute.Value);
                return resourceKey is not null && themeBrushKeys.Contains(resourceKey);
            });
        Assert.Contains(
            attributes,
            attribute => themeBrushKeys.Any(key =>
                string.Equals(
                    attribute.Value,
                    $"{{DynamicResource {key}}}",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void AccountAndCatalogItems_ExposeStableAutomationLabels()
    {
        var launcher = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var linkButton = launcher
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value.Contains(
                    "LinkMinecraftCommand",
                    StringComparison.Ordinal) == true);
        Assert.Equal(
            "绑定 Microsoft 正版身份",
            linkButton.Attribute("AutomationProperties.Name")?.Value);

        foreach (var listName in new[] { "服务器目录", "下载历史" })
        {
            var list = launcher
                .Descendants(presentation + "ListBox")
                .Single(element =>
                    element.Attribute("AutomationProperties.Name")?.Value == listName);
            var setters = list
                .Descendants(presentation + "Setter")
                .Select(setter => setter.Attribute("Property")?.Value)
                .ToArray();

            Assert.Contains("AutomationProperties.Name", setters);
            Assert.Contains("AutomationProperties.ItemStatus", setters);
            Assert.Contains("AutomationProperties.HelpText", setters);
        }

        var activityItemStyle = launcher
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ActivityCalendarDetailItemStyle");
        var activityAutomationSetters = activityItemStyle
            .Elements(presentation + "Setter")
            .Select(setter => setter.Attribute("Property")?.Value)
            .ToArray();
        Assert.Contains("AutomationProperties.Name", activityAutomationSetters);
        Assert.Contains("AutomationProperties.ItemStatus", activityAutomationSetters);
        Assert.Contains("AutomationProperties.HelpText", activityAutomationSetters);

        foreach (var listName in new[] { "所选日期的活动", "待排期活动" })
        {
            var list = launcher
                .Descendants(presentation + "ListBox")
                .Single(element =>
                    element.Attribute("AutomationProperties.Name")?.Value == listName);
            Assert.Equal(
                "{StaticResource ActivityCalendarDetailItemStyle}",
                list.Attribute("ItemContainerStyle")?.Value);
        }
    }

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
    public void AccountProfileAvatar_UsesLoadedMinecraftSkinWithFallback()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var avatar = document
            .Descendants()
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "AccountProfileAvatar");
        var skinBrushes = avatar
            .Descendants(presentation + "ImageBrush")
            .ToArray();

        Assert.Equal("Minecraft 皮肤头像", avatar.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(2, skinBrushes.Length);
        Assert.All(skinBrushes, brush =>
            Assert.Equal("{Binding AccountSkinSource}", brush.Attribute("ImageSource")?.Value));
        Assert.Contains(skinBrushes, brush => brush.Attribute("Viewbox")?.Value == "8,8,8,8");
        Assert.Contains(skinBrushes, brush => brush.Attribute("Viewbox")?.Value == "40,8,8,8");
        Assert.Contains("HasAccountSkin", avatar.ToString(), StringComparison.Ordinal);
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
            .Descendants()
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "LauncherUpdateProgressRegion");
        var progressBar = progressRegion
            .Descendants()
            .Single(element => element.Name.LocalName == "AnimatedProgressBar");
        var status = progressRegion
            .Descendants(presentation + "TextBlock")
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
    public void Branding_UsesThemeAwareOfficialLockupAndCrispMultiSizeAppIcon()
    {
        const string darkLockupSource =
            "/Hechao.Launcher;component/Assets/hechao-launcher-lockup-hd-dark.png";
        const string lightLockupSource =
            "/Hechao.Launcher;component/Assets/hechao-launcher-lockup-hd-light.png";
        const string appIconSource =
            "/Hechao.Launcher;component/Assets/hechao-launcher.ico";
        var repositoryRoot = FindRepositoryRoot();
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Equal(appIconSource, document.Root?.Attribute("Icon")?.Value);

        var brandLockup = document
            .Descendants(presentation + "Image")
            .Single(image => image.Attribute("AutomationProperties.Name")?.Value ==
                "赫朝品牌标识");
        Assert.Equal("75", brandLockup.Attribute("Width")?.Value);
        Assert.Equal("37", brandLockup.Attribute("Height")?.Value);
        Assert.Equal("Uniform", brandLockup.Attribute("Stretch")?.Value);
        Assert.Equal("True", brandLockup.Attribute("SnapsToDevicePixels")?.Value);
        var lockupSources = brandLockup
            .Descendants(presentation + "Setter")
            .Where(setter => setter.Attribute("Property")?.Value == "Source")
            .Select(setter => setter.Attribute("Value")?.Value)
            .ToArray();
        Assert.Contains(darkLockupSource, lockupSources);
        Assert.Contains(lightLockupSource, lockupSources);
        Assert.DoesNotContain(
            document.Descendants(presentation + "Image"),
            image => image.Attribute("Source")?.Value?.EndsWith(
                "/hechao-launcher-icon.png",
                StringComparison.Ordinal) == true);

        var project = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "Hechao.Launcher.csproj"));
        var resources = project
            .Descendants("Resource")
            .Select(resource => resource.Attribute("Include")?.Value)
            .ToArray();
        Assert.Contains(@"Assets\hechao-launcher-lockup-37h.png", resources);
        Assert.Contains(@"Assets\hechao-launcher-lockup-hd-dark.png", resources);
        Assert.Contains(@"Assets\hechao-launcher-lockup-hd-light.png", resources);

        var generator = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "Generate-AppIcon.ps1"));
        Assert.Contains("#D74735", generator, StringComparison.Ordinal);
        Assert.Contains("#24211F", generator, StringComparison.Ordinal);
        Assert.Contains("#FFFBF5", generator, StringComparison.Ordinal);
        Assert.Contains("SmoothingMode]::None", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("#AB251E", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("New-RoundedPath", generator, StringComparison.Ordinal);

        var iconPath = Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "Assets",
            "hechao-launcher.ico");
        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.Equal((ushort)7, count);

        var frames = new List<(int Size, uint Length, uint Offset)>();
        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            Assert.Equal((ushort)1, reader.ReadUInt16());
            Assert.Equal((ushort)32, reader.ReadUInt16());
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var size = width == 0 ? 256 : width;
            Assert.Equal(size, height == 0 ? 256 : height);
            frames.Add((size, length, offset));
        }

        Assert.Equal(
            new[] { 16, 24, 32, 48, 64, 128, 256 },
            frames.Select(frame => frame.Size));
        foreach (var frame in frames)
        {
            Assert.True(frame.Length > 8);
            stream.Position = frame.Offset;
            Assert.Equal(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                reader.ReadBytes(8));
        }
    }

    [Fact]
    public void ServerMaintenanceActions_AreConsolidatedInPrimaryActionMenu()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var actionButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerActionsButton");
        var menu = document
            .Descendants(presentation + "ContextMenu")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerActionsContextMenu");
        var menuItems = menu.Elements(presentation + "MenuItem").ToArray();

        Assert.Contains(actionButton, menu.Ancestors());
        Assert.Equal("ServerActionsButton_OnClick", actionButton.Attribute("Click")?.Value);
        Assert.Equal("打开客户端操作菜单", actionButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Contains(
            actionButton.Descendants(),
            element =>
                element.Name.LocalName == "IconParkIcon" &&
                element.Attribute("Kind")?.Value == "More");
        Assert.Equal(
            [
                "回滚客户端",
                "校验并修复客户端",
                "删除客户端",
                "客户端设置",
                "Java 自动",
                "Java 自定义",
            ],
            menuItems.Select(item => item.Attribute("Header")?.Value));
        Assert.Equal(
            "{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}",
            menu.Attribute("DataContext")?.Value);
    }

    [Fact]
    public void ServerActionMenu_StaysReachableInFixedInspector()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var actionButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ServerActionsButton");
        var primaryActionButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerPrimaryActionButton");
        var actionGrid = actionButton
            .Ancestors(presentation + "Grid")
            .First();
        var columnWidths = actionGrid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Select(element => element.Attribute("Width")?.Value ?? string.Empty)
            .ToArray();
        var menuStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ServerActionMenuStyle");
        var workspace = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerWorkspace");
        var rootGrid = Assert.IsType<XElement>(workspace.Parent);
        var rootColumns = rootGrid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Select(element => element.Attribute("Width")?.Value ?? string.Empty)
            .ToArray();
        var workspaceRows = workspace
            .Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .Select(element => element.Attribute("Height")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(["38", "*", "38"], columnWidths);
        Assert.Equal(["0", "*", "420"], rootColumns);
        Assert.Equal(["56", "*", "76"], workspaceRows);
        Assert.Equal("2", workspace.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", workspace.Attribute("Grid.Row")?.Value);
        Assert.Equal("34", actionButton.Attribute("Width")?.Value);
        Assert.Equal("46", primaryActionButton.Attribute("Height")?.Value);
        Assert.Equal("22,15", primaryActionButton.Attribute("Margin")?.Value);
        Assert.Equal("18,8", primaryActionButton.Attribute("Padding")?.Value);
        Assert.Contains(
            menuStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Width" &&
                setter.Attribute("Value")?.Value == "228");
    }

    [Fact]
    public void ServerHome_RemovesLegacyReadinessAndRuntimePanels()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute(x + "Name")?.Value is
                "ServerDetailsScrollViewer" or
                "ClientReadinessPanel" or
                "ClientProfileActionsPanel");
        Assert.DoesNotContain(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value is
                "客户端准备" or "运行配置");
    }

    [Fact]
    public void Theme_UsesVisibleFocusVisualAndSlimScrollbar()
    {
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var focusVisualStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "HechaoFocusVisualStyle");
        Assert.Contains(
            focusVisualStyle.Descendants(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Control.Template");
        Assert.DoesNotContain(
            document.Descendants(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                setter.Attribute("Value")?.Value == "{x:Null}");
        Assert.All(
            document.Descendants(presentation + "Setter")
                .Where(setter =>
                    setter.Attribute("Property")?.Value == "FocusVisualStyle"),
            setter => Assert.Equal(
                "{StaticResource HechaoFocusVisualStyle}",
                setter.Attribute("Value")?.Value));

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
    public void TopBarAccountEntry_UsesStableCompactIdentity()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var titleBar = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "TitleBar");
        var accountButton = titleBar
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value ==
                "{Binding ShowAccountCommand}");

        Assert.Equal("126", accountButton.Attribute("Width")?.Value);
        Assert.Equal("52", accountButton.Attribute("Height")?.Value);
        Assert.Equal(
            "赫朝账户",
            accountButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Contains(
            accountButton.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding AccountDisplayName}");
        Assert.Contains(
            accountButton.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding TopBarAccountSubtitle}");
        var skinBrushes = accountButton
            .Descendants(presentation + "ImageBrush")
            .ToArray();
        Assert.Equal(2, skinBrushes.Length);
        Assert.All(skinBrushes, brush =>
            Assert.Equal(
                "{Binding AccountSkinSource}",
                brush.Attribute("ImageSource")?.Value));
        Assert.Contains(
            accountButton.Descendants(),
            element => element.Name.LocalName == "IconParkIcon" &&
                element.Attribute("Kind")?.Value == "User");
        Assert.Contains("HasAccountSkin", accountButton.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute(x + "Name")?.Value ==
                "SidebarAccountAvatar");
    }

    [Fact]
    public void ServerHome_UsesCatalogAndInspectorWithoutLegacyRail()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var titleBar = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "TitleBar");
        var directoryPanel = document
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerDirectoryPanel");
        var inspector = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerWorkspace");
        var heroPanel = document
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerHeroPanel");
        var heroImage = heroPanel
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerHeroImage");
        var heroDetails = heroPanel
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerHeroDetails");
        var serverCardTemplate = document
            .Descendants(presentation + "DataTemplate")
            .Single(element =>
                element.Attribute("DataType")?.Value ==
                "{x:Type contracts:ServerSummary}" &&
                element.Ancestors(presentation + "ListBox").Any(list =>
                    list.Attribute("ItemsSource")?.Value ==
                    "{Binding Servers}"));
        var serverCardCover = serverCardTemplate
            .Descendants(presentation + "Image")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ServerCardCoverImage");
        var activityArtwork = serverCardTemplate
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ActivityServerCardArtwork");
        var serverCardShortNameBadge = serverCardTemplate
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ServerCardShortNameBadge");

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute(x + "Name")?.Value is
                "NavigationRail" or "NavigationAccountPanel");
        Assert.Contains(
            titleBar.Descendants(presentation + "Button"),
            element => element.Attribute("Command")?.Value ==
                "{Binding ShowAccountCommand}");
        Assert.Equal("1", directoryPanel.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", directoryPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", inspector.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", inspector.Attribute("Grid.Row")?.Value);
        Assert.Contains(heroPanel.Ancestors(), ancestor => ReferenceEquals(ancestor, inspector));
        Assert.Equal("22,18,16,28", heroPanel.Attribute("Padding")?.Value);
        Assert.Equal("184", heroImage.Attribute("Height")?.Value);
        Assert.Equal("7", heroImage.Attribute("CornerRadius")?.Value);
        Assert.Equal("True", heroImage.Attribute("ClipToBounds")?.Value);
        Assert.Empty(heroImage.Descendants(presentation + "TextBlock"));
        Assert.Contains(
            heroImage.Descendants(presentation + "ImageBrush"),
            brush => brush.Attribute("ImageSource")?.Value ==
                "/Hechao.Launcher;component/Assets/hechao-fortress-banner.png");
        Assert.Contains(
            heroDetails.Descendants(presentation + "TextBlock"),
            text => text.Attribute("Text")?.Value ==
                "{Binding SelectedServerCategoryText}");
        foreach (var activityVisualElement in new[]
                 {
                     (Element: serverCardCover, Visibility: "Collapsed"),
                     (Element: activityArtwork, Visibility: "Visible"),
                     (Element: serverCardShortNameBadge, Visibility: "Collapsed"),
                 })
        {
            var trigger = Assert.Single(
                activityVisualElement.Element.Descendants(
                    presentation + "DataTrigger"),
                trigger =>
                    trigger.Attribute("Binding")?.Value ==
                    "{Binding Converter={StaticResource ServerIsActivityConverter}}" &&
                    trigger.Attribute("Value")?.Value == "True");
            Assert.Contains(
                trigger.Elements(presentation + "Setter"),
                setter =>
                    setter.Attribute("Property")?.Value == "Visibility" &&
                    setter.Attribute("Value")?.Value ==
                    activityVisualElement.Visibility);
        }
        Assert.Contains(
            activityArtwork.Descendants(presentation + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding IconGlyph}");
        Assert.Contains(
            inspector.Descendants(presentation + "Button"),
            button => button.Attribute("Command")?.Value ==
                "{Binding PrimaryActionCommand}");
    }

    [Fact]
    public void ServerActionMenu_ProvidesConfirmedClientRemovalAction()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var menuItem = document
            .Descendants(presentation + "MenuItem")
            .Single(element =>
                element.Attribute("Click")?.Value == "DeleteProfileButton_OnClick");

        Assert.Equal(
            "{Binding CanDeleteSelectedProfile}",
            menuItem.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding DeleteProfileToolTip}",
            menuItem.Attribute("ToolTip")?.Value);
        Assert.Contains(
            menuItem.Descendants(),
            element => element.Attribute("Kind")?.Value == "Delete");
    }

    [Fact]
    public void ServerQuickSettings_UsesSelectedProfileDirectoryAndSingleOpenAction()
    {
        var launcher = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var gameDirectory = launcher
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeClientDirectoryText");
        var quickSettingsPanel = launcher
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeQuickSettingsPanel");
        var openDirectoryButton = quickSettingsPanel
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "OpenHomeProfileDirectoryButton");

        Assert.Equal(
            "CharacterEllipsis",
            gameDirectory.Attribute("TextTrimming")?.Value);
        Assert.Equal(
            "{Binding SelectedProfileGameDirectoryDisplayText}",
            gameDirectory.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding SelectedProfileGameDirectoryDisplayText}",
            gameDirectory.Attribute("ToolTip")?.Value);
        Assert.Equal(
            "{Binding SelectedProfileGameDirectoryDisplayText}",
            gameDirectory.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Equal(
            "{Binding OpenSelectedProfileGameDirectoryCommand}",
            openDirectoryButton.Attribute("Command")?.Value);
        Assert.DoesNotContain(
            quickSettingsPanel.Descendants(presentation + "Button"),
            element => element.Attribute("Click")?.Value ==
                "ChooseClientDirectoryButton_OnClick");
    }

    [Fact]
    public void SettingsWorkspace_UsesFourCategoriesAndPageLocalScrollers()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var settingsWorkspace = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "SettingsWorkspace");
        var settingsTabs = settingsWorkspace
            .Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "SettingsWorkspaceTabs");
        var tabs = settingsTabs
            .Elements(presentation + "TabItem")
            .ToArray();
        var headers = tabs
            .Select(tab => tab
                .Elements()
                .Single(element => element.Name.LocalName == "TabItem.Header")
                .Descendants(presentation + "TextBlock")
                .Select(element => element.Attribute("Text")?.Value ?? string.Empty)
                .First())
            .ToArray();
        var scrollViewers = settingsTabs
            .Descendants(presentation + "ScrollViewer")
            .ToArray();

        Assert.Equal("2", settingsTabs.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("0", settingsTabs.Attribute("SelectedIndex")?.Value);
        Assert.Equal(
            ["SettingsGameTab", "SettingsClientTab", "SettingsBehaviorTab", "SettingsDiagnosticsTab"],
            tabs.Select(tab => tab.Attribute(x + "Name")?.Value ?? string.Empty));
        Assert.Equal(
            ["游戏与运行", "客户端与下载", "启动器行为", "故障诊断"],
            headers);
        Assert.All(
            tabs,
            tab => Assert.Equal(
                "{StaticResource SettingsCategoryTabItemStyle}",
                tab.Attribute("Style")?.Value));
        Assert.Empty(settingsWorkspace.Elements(presentation + "ScrollViewer"));
        Assert.Equal(
            [
                "SettingsGameScrollViewer",
                "SettingsClientScrollViewer",
                "SettingsBehaviorScrollViewer",
                "SettingsDiagnosticsScrollViewer",
            ],
            scrollViewers.Select(element => element.Attribute(x + "Name")?.Value ?? string.Empty));
        Assert.All(
            scrollViewers,
            scrollViewer =>
            {
                Assert.Equal("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
                Assert.Equal("Disabled", scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
            });
    }

    [Fact]
    public void SettingsWorkspace_PreservesBindingsAndRiskGuards()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var settingsTabs = document
            .Descendants(presentation + "TabControl")
            .Where(element =>
                element.Attribute(x + "Name")?.Value == "SettingsWorkspaceTabs")
            .Single();
        var tabs = settingsTabs
            .Elements(presentation + "TabItem")
            .ToArray();
        var expectedBindings = new Dictionary<string, string[]>
        {
            ["SettingsGameTab"] =
            [
                "{Binding MemoryOptions}",
                "{Binding SelectedMemory, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
                "{Binding CloseLauncherAfterGameStart, Mode=TwoWay}",
            ],
            ["SettingsClientTab"] =
            [
                "{Binding ClientDirectory}",
                "{Binding CheckForUpdates, Mode=TwoWay}",
                "{Binding KeepDownloadsAfterClose, Mode=TwoWay}",
                "{Binding OpenDownloadsWhenInstalling, Mode=TwoWay}",
                "{Binding UseSystemProxy, Mode=TwoWay}",
                "{Binding CheckLauncherUpdateCommand}",
            ],
            ["SettingsBehaviorTab"] =
            [
                "{Binding StartupPageOptions}",
                "{Binding SelectedStartupPage, Mode=TwoWay}",
                "{Binding OpenClientDirectoryCommand}",
                "{Binding ClearDownloadHistoryCommand}",
                "{Binding ResetLauncherSettingsCommand}",
            ],
            ["SettingsDiagnosticsTab"] =
            [
                "{Binding LatestGameExitText}",
                "{Binding DiagnosticUploadStatus}",
                "{Binding CreateDiagnosticBundleCommand}",
                "{Binding CanUploadDiagnosticBundle}",
                "{Binding OpenDiagnosticsDirectoryCommand}",
            ],
        };

        foreach (var (tabName, bindings) in expectedBindings)
        {
            var tab = tabs.Single(element => element.Attribute(x + "Name")?.Value == tabName);
            var attributes = tab
                .DescendantsAndSelf()
                .Attributes()
                .Select(attribute => attribute.Value)
                .ToArray();
            Assert.All(
                bindings,
                binding => Assert.Contains(binding, attributes));
        }

        var switches = tabs
            .SelectMany(tab => tab.Descendants(presentation + "ToggleButton"))
            .Where(element => element.Attribute("Style")?.Value ==
                "{StaticResource SwitchToggleStyle}")
            .ToArray();
        Assert.Equal(
            [
                "{Binding CloseLauncherAfterGameStart, Mode=TwoWay}",
                "{Binding CheckForUpdates, Mode=TwoWay}",
                "{Binding KeepDownloadsAfterClose, Mode=TwoWay}",
                "{Binding OpenDownloadsWhenInstalling, Mode=TwoWay}",
                "{Binding UseSystemProxy, Mode=TwoWay}",
            ],
            switches.Select(element => element.Attribute("IsChecked")?.Value ?? string.Empty));
        Assert.All(
            switches,
            element => Assert.False(string.IsNullOrWhiteSpace(
                element.Attribute("AutomationProperties.Name")?.Value)));

        var clientTab = tabs.Single(element => element.Attribute(x + "Name")?.Value ==
            "SettingsClientTab");
        var changeDirectoryButton = clientTab
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "更改目录");
        Assert.Equal(
            "{Binding CanChangeClientDirectory}",
            changeDirectoryButton.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "ChooseClientDirectoryButton_OnClick",
            changeDirectoryButton.Attribute("Click")?.Value);
        Assert.Equal("更改游戏数据目录", changeDirectoryButton.Attribute(
            "AutomationProperties.Name")?.Value);

        var behaviorTab = tabs.Single(element => element.Attribute(x + "Name")?.Value ==
            "SettingsBehaviorTab");
        foreach (var command in new[]
                 {
                     "{Binding ClearDownloadHistoryCommand}",
                     "{Binding ResetLauncherSettingsCommand}",
                 })
        {
            var button = behaviorTab
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute("Command")?.Value == command);
            Assert.Equal(
                "{StaticResource DangerButtonStyle}",
                button.Attribute("Style")?.Value);
        }

        var diagnosticsTab = tabs.Single(element => element.Attribute(x + "Name")?.Value ==
            "SettingsDiagnosticsTab");
        var uploadButton = diagnosticsTab
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value ==
                "UploadDiagnosticButton_OnClick");
        var createButton = diagnosticsTab
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Command")?.Value ==
                "{Binding CreateDiagnosticBundleCommand}");
        Assert.Equal(
            "{Binding CanUploadDiagnosticBundle}",
            uploadButton.Attribute("IsEnabled")?.Value);
        Assert.Null(uploadButton.Attribute("Command"));
        Assert.Null(createButton.Attribute("Click"));
        Assert.Single(
            clientTab.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value == "保留已下载文件缓存");
        Assert.Contains(
            clientTab.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value ==
                "保留校验通过的下载文件，供后续安装、更新或修复复用。");
    }

    [Fact]
    public void SettingsTheme_UsesSplitWorkspaceSelectionAndVisibleTitleForeground()
    {
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var workspaceStyle = document
            .Descendants(presentation + "Style")
            .Single(element => element.Attribute(x + "Key")?.Value ==
                "SettingsWorkspaceTabControlStyle");
        var templateGrid = workspaceStyle
            .Descendants(presentation + "Grid")
            .Single();
        Assert.Equal(
            ["*", "520"],
            templateGrid
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Select(element => element.Attribute("Width")?.Value));
        var leftSurface = templateGrid
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute("Grid.Column")?.Value == "0");
        var rightSurface = templateGrid
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute("Grid.Column")?.Value == "1");
        Assert.Equal("{DynamicResource DirectoryBrush}", leftSurface.Attribute("Background")?.Value);
        Assert.Equal("0,0,1,0", leftSurface.Attribute("BorderThickness")?.Value);
        Assert.Equal("{DynamicResource SurfaceBrush}", rightSurface.Attribute("Background")?.Value);
        Assert.Equal(
            "PART_SelectedContentHost",
            rightSurface
                .Descendants(presentation + "ContentPresenter")
                .Single()
                .Attribute(x + "Name")?.Value);

        var categoryStyle = document
            .Descendants(presentation + "Style")
            .Single(element => element.Attribute(x + "Key")?.Value ==
                "SettingsCategoryTabItemStyle");
        Assert.Contains(
            categoryStyle.Elements(presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Height" &&
                setter.Attribute("Value")?.Value == "62");
        Assert.Contains(
            categoryStyle.Elements(presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                setter.Attribute("Value")?.Value == "{StaticResource HechaoFocusVisualStyle}");
        Assert.Contains(
            categoryStyle.Elements(presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment" &&
                setter.Attribute("Value")?.Value == "Stretch");
        Assert.Contains(
            categoryStyle.Descendants(presentation + "Border"),
            border => border.Attribute(x + "Name")?.Value == "CategoryMarker");
        Assert.Contains(
            categoryStyle.Descendants(presentation + "Trigger"),
            trigger => trigger.Attribute("Property")?.Value == "IsSelected");

        foreach (var key in new[] { "PageHeaderTitleStyle", "SectionTitleStyle" })
        {
            var titleStyle = document
                .Descendants(presentation + "Style")
                .Single(element => element.Attribute(x + "Key")?.Value == key);
            Assert.Contains(
                titleStyle.Elements(presentation + "Setter"),
                setter => setter.Attribute("Property")?.Value == "Foreground" &&
                    setter.Attribute("Value")?.Value == "{DynamicResource InkBrush}");
        }
    }

    [Fact]
    public void DownloadsWorkspace_UsesDirectoryAndFixedInspectorWithoutOuterScroll()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var directory = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "DownloadDirectoryPanel");
        var inspector = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "DownloadInspector");
        var rootGrid = Assert.IsType<XElement>(directory.Parent);
        var history = directory
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute("ItemsSource")?.Value ==
                "{Binding DownloadHistory}");
        var inspectorScrollViewer = inspector
            .Descendants(presentation + "ScrollViewer")
            .Single();

        Assert.Equal("1", directory.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", directory.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", inspector.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", inspector.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            ["0", "*", "420"],
            rootGrid
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Select(element => element.Attribute("Width")?.Value ?? string.Empty));
        Assert.Equal(
            ["90", "*"],
            directory
                .Element(presentation + "Grid")!
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value ?? string.Empty));
        Assert.Empty(directory.Descendants(presentation + "ScrollViewer"));
        Assert.Equal("Auto", history.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", history.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            ["56", "*", "76"],
            inspector
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value ?? string.Empty));
        Assert.Equal("Auto", inspectorScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", inspectorScrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void ActivityWorkspace_UsesSevenColumnCalendarAndFixedInspectorWithoutOuterScroll()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var calendarPanel = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "ActivityCalendarPanel");
        var inspector = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "ActivityInspector");
        var rootGrid = Assert.IsType<XElement>(calendarPanel.Parent);
        var calendar = calendarPanel
            .Descendants(presentation + "ItemsControl")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "ActivityCalendarDays");
        var calendarGrid = calendar
            .Element(presentation + "ItemsControl.ItemsPanel")!
            .Descendants(presentation + "UniformGrid")
            .Single();
        var detailScrollViewer = inspector
            .Descendants(presentation + "ScrollViewer")
            .Single();
        var detailLists = inspector
            .Descendants(presentation + "ListBox")
            .Where(element => element.Attribute(x + "Name")?.Value is
                "SelectedActivityList" or "UnscheduledActivityList")
            .ToArray();

        Assert.Equal("1", calendarPanel.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", calendarPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", inspector.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", inspector.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            ["0", "*", "420"],
            rootGrid
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Select(element => element.Attribute("Width")?.Value ?? string.Empty));
        Assert.Equal("{Binding ActivityCalendar.Days}", calendar.Attribute("ItemsSource")?.Value);
        Assert.Equal("7", calendarGrid.Attribute("Columns")?.Value);
        Assert.Empty(calendarPanel.Descendants(presentation + "ScrollViewer"));
        Assert.Equal("Auto", detailScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", detailScrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(2, detailLists.Length);
        Assert.All(
            detailLists,
            list =>
            {
                Assert.Equal("Disabled", list.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
                Assert.Equal("Disabled", list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
            });
    }

    [Fact]
    public void ActivityWorkspace_UsesQuietDividersWithoutBoxingEveryCalendarDay()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var dayStyle = document
            .Descendants(presentation + "Style")
            .Single(element => element.Attribute(x + "Key")?.Value ==
                "ActivityCalendarDayButtonStyle");
        var daySurface = dayStyle
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value == "DaySurface");
        Assert.Equal(
            "{DynamicResource HairlineBrush}",
            daySurface.Attribute("BorderBrush")?.Value);
        Assert.Equal("0,0,0,1", daySurface.Attribute("BorderThickness")?.Value);

        foreach (var borderName in new[]
                 {
                     "ActivityCalendarPanel",
                     "ActivityCalendarSurface",
                     "ActivityCalendarMonthHeader",
                     "ActivityCalendarWeekHeader",
                     "ActivityInspectorHeader",
                     "ActivitySelectedDateHeader",
                     "ActivityUnscheduledSection",
                 })
        {
            var border = document
                .Descendants(presentation + "Border")
                .Single(element => element.Attribute(x + "Name")?.Value == borderName);
            Assert.Equal(
                "{DynamicResource DividerBrush}",
                border.Attribute("BorderBrush")?.Value);
        }

        var detailItemSurface = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "ActivityDetailItemSurface");
        Assert.Equal(
            "{DynamicResource HairlineBrush}",
            detailItemSurface.Attribute("BorderBrush")?.Value);
        Assert.Equal("22,0,18,0", detailItemSurface.Attribute("Margin")?.Value);
        Assert.Equal("0,18", detailItemSurface.Attribute("Padding")?.Value);

        var dividerBrush = LoadThemeXaml()
            .Descendants(presentation + "SolidColorBrush")
            .Single(element => element.Attribute(x + "Key")?.Value == "DividerBrush");
        Assert.Equal(
            "{DynamicResource DividerColor}",
            dividerBrush.Attribute("Color")?.Value);

        var hairlineBrush = LoadThemeXaml()
            .Descendants(presentation + "SolidColorBrush")
            .Single(element => element.Attribute(x + "Key")?.Value == "HairlineBrush");
        Assert.Equal(
            "{DynamicResource HairlineColor}",
            hairlineBrush.Attribute("Color")?.Value);

        var expectedLineColors = new Dictionary<string, (string Divider, string Hairline)>
        {
            ["DarkPalette.xaml"] = ("#FF32383E", "#FF252A2E"),
            ["LightPalette.xaml"] = ("#FFCBD6DD", "#FFE3E9ED"),
        };
        foreach (var (paletteName, expectedColors) in expectedLineColors)
        {
            var colors = LoadPaletteXaml(paletteName)
                .Descendants(presentation + "Color")
                .ToDictionary(
                    element => element.Attribute(x + "Key")!.Value,
                    element => element.Value);
            Assert.Equal(expectedColors.Divider, colors["DividerColor"]);
            Assert.Equal(expectedColors.Hairline, colors["HairlineColor"]);
        }
    }

    [Fact]
    public void AccountWorkspace_UsesAuthBranchesAndExplicitDangerHierarchy()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var directory = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "AccountDirectoryPanel");
        var inspector = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value ==
                "AccountInspector");
        var rootGrid = Assert.IsType<XElement>(directory.Parent);
        var directoryBranches = directory
            .Descendants(presentation + "Grid")
            .Where(element => element
                .Element(presentation + "Grid.Style")?
                .Descendants(presentation + "DataTrigger")
                .Any(trigger => trigger.Attribute("Binding")?.Value ==
                    "{Binding IsAuthenticated}") == true)
            .ToArray();
        var inspectorBranches = inspector
            .Descendants(presentation + "Grid")
            .Where(element => element
                .Element(presentation + "Grid.Style")?
                .Descendants(presentation + "DataTrigger")
                .Any(trigger => trigger.Attribute("Binding")?.Value ==
                    "{Binding IsAuthenticated}") == true)
            .ToArray();

        Assert.Equal("1", directory.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", directory.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", inspector.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", inspector.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            ["0", "*", "420"],
            rootGrid
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Select(element => element.Attribute("Width")?.Value ?? string.Empty));
        Assert.Equal(2, directoryBranches.Length);
        Assert.Equal(2, inspectorBranches.Length);
        Assert.Contains(
            directoryBranches,
            branch =>
                branch.Element(presentation + "Grid.Style")!
                    .Element(presentation + "Style")!
                    .Elements(presentation + "Setter")
                    .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                        setter.Attribute("Value")?.Value == "Visible") &&
                branch.Element(presentation + "Grid.Style")!
                    .Descendants(presentation + "DataTrigger")
                    .Any(trigger => trigger.Descendants(presentation + "Setter")
                        .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                            setter.Attribute("Value")?.Value == "Collapsed")));
        Assert.Contains(
            directoryBranches,
            branch =>
                branch.Element(presentation + "Grid.Style")!
                    .Element(presentation + "Style")!
                    .Elements(presentation + "Setter")
                    .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                        setter.Attribute("Value")?.Value == "Collapsed") &&
                branch.Element(presentation + "Grid.Style")!
                    .Descendants(presentation + "DataTrigger")
                    .Any(trigger => trigger.Descendants(presentation + "Setter")
                        .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                            setter.Attribute("Value")?.Value == "Visible")));
        Assert.All(
            inspectorBranches,
            branch => Assert.Contains(
                branch.Descendants(presentation + "DataTrigger"),
                trigger => trigger.Attribute("Binding")?.Value ==
                    "{Binding IsAuthenticated}"));

        var authenticatedDirectory = directoryBranches.Single(branch =>
            branch.Element(presentation + "Grid.Style")!
                .Element(presentation + "Style")!
                .Elements(presentation + "Setter")
                .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                    setter.Attribute("Value")?.Value == "Collapsed"));
        var authenticatedDirectoryScroller = Assert.IsType<XElement>(authenticatedDirectory.Parent);
        Assert.Equal(presentation + "ScrollViewer", authenticatedDirectoryScroller.Name);
        Assert.Equal(
            "Auto",
            authenticatedDirectoryScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Disabled",
            authenticatedDirectoryScroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        var unauthenticatedDirectory = directoryBranches.Single(branch =>
            branch.Element(presentation + "Grid.Style")!
                .Element(presentation + "Style")!
                .Elements(presentation + "Setter")
                .Any(setter => setter.Attribute("Property")?.Value == "Visibility" &&
                    setter.Attribute("Value")?.Value == "Visible"));
        Assert.DoesNotContain(
            unauthenticatedDirectory.Ancestors(),
            ancestor => ancestor.Name == presentation + "ScrollViewer");

        var logoutButton = authenticatedDirectory
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Command")?.Value ==
                "{Binding LogoutAccountCommand}");
        var logoutAllButton = authenticatedDirectory
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Command")?.Value ==
                "{Binding LogoutAllDevicesCommand}");
        Assert.Equal("{StaticResource BaseButtonStyle}", logoutButton.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource DangerButtonStyle}", logoutAllButton.Attribute("Style")?.Value);
    }

    [Fact]
    public void GlobalOverlays_TrapTabFocusAtTheRoot()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootGrid = document.Root!
            .Element(presentation + "Border")!
            .Element(presentation + "Grid")!;

        foreach (var name in new[]
                 {
                     "MicrosoftSignInOverlay",
                     "LauncherUpdateOverlay",
                 })
        {
            var overlay = document
                .Descendants()
                .Single(element => element.Attribute(x + "Name")?.Value == name);
            Assert.Same(rootGrid, overlay.Parent);
            Assert.Equal(
                "True",
                overlay.Attribute("FocusManager.IsFocusScope")?.Value);
            Assert.Contains(
                "ElementName=",
                overlay.Attribute("FocusManager.FocusedElement")?.Value);
            Assert.Equal(
                "Cycle",
                overlay.Attribute("KeyboardNavigation.TabNavigation")?.Value);
        }
    }

    [Fact]
    public void StatusAndToast_UseSemanticStateBindings()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var statusText = document
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute("Text")?.Value ==
                "{Binding SelectedServerStatusText}");
        var statusValues = statusText
            .Descendants(presentation + "DataTrigger")
            .Select(element => element.Attribute("Value")?.Value)
            .ToArray();
        Assert.Contains(
            "{x:Static contracts:ServerStatus.Online}",
            statusValues);
        Assert.Contains(
            "{x:Static contracts:ServerStatus.Maintenance}",
            statusValues);
        Assert.Contains(
            statusText.Descendants(presentation + "Setter"),
            element =>
                element.Attribute("Property")?.Value == "Foreground" &&
                element.Attribute("Value")?.Value ==
                "{DynamicResource ClosedBrush}");

        var toast = document
            .Descendants()
            .Single(element =>
                element.Attribute("AutomationProperties.Name")?.Value ==
                "{Binding ToastMessage}");
        var toastIcon = toast
            .Descendants()
            .Single(element => element.Name.LocalName == "IconParkIcon");
        Assert.Equal(
            "{Binding ToastSeverity}",
            toast.Attribute("Tag")?.Value);
        Assert.Equal(
            "{Binding ToastMessage}",
            toast.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{Binding ToastAutomationStatus}",
            toast.Attribute("AutomationProperties.ItemStatus")?.Value);
        Assert.Equal(
            "Polite",
            toast.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Equal(
            "{Binding ToastIconKind}",
            toastIcon.Attribute("Kind")?.Value);
        Assert.Equal(
            "{DynamicResource ToastBrush}",
            toast.Attribute("Background")?.Value);
        Assert.Equal(
            "{DynamicResource ToastIconBrush}",
            toastIcon.Attribute("Foreground")?.Value);

        var progressSteps = document
            .Descendants(presentation + "Ellipse")
            .Where(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource ProgressStepIndicatorStyle}")
            .ToArray();
        Assert.Empty(progressSteps);

        var progressGlyphs = document
            .Descendants(presentation + "TextBlock")
            .Where(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource ProgressStepGlyphStyle}")
            .ToArray();
        Assert.Empty(progressGlyphs);
    }

    [Fact]
    public void ServerAndActivitySelection_RespectLongRunningTasks()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var serverList = document
            .Descendants(presentation + "ListBox")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding Servers}");
        var activityLists = document
            .Descendants(presentation + "ListBox")
            .Where(element =>
                element.Attribute(x + "Name")?.Value is
                    "SelectedActivityList" or "UnscheduledActivityList")
            .ToArray();
        Assert.Equal(
            "{Binding CanSelectServer}",
            serverList.Attribute("IsEnabled")?.Value);
        Assert.Equal(2, activityLists.Length);
        Assert.All(
            activityLists,
            activityList => Assert.Equal(
                "{Binding CanSelectServer}",
                activityList.Attribute("IsEnabled")?.Value));

        var detailTemplate = document
            .Descendants(presentation + "DataTemplate")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ActivityCalendarDetailTemplate");
        var prepareButton = detailTemplate
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value.Contains(
                    "PrepareActivityClientCommand",
                    StringComparison.Ordinal) == true);
        Assert.Equal(
            "{Binding CanPrepareClient}",
            prepareButton.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding}",
            prepareButton.Attribute("CommandParameter")?.Value);
    }

    [Fact]
    public void ActivityCalendarDetails_UseWrapperServerAndShowScheduleDetails()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document
            .Descendants(presentation + "DataTemplate")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ActivityCalendarDetailTemplate");
        var attributeValues = template
            .DescendantsAndSelf()
            .Attributes()
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding ScheduleText}", attributeValues);
        Assert.Contains("{Binding AnnouncementText}", attributeValues);
        Assert.Contains("{Binding Server.Name}", attributeValues);
        Assert.Contains("{Binding Server.MinecraftVersion}", attributeValues);
        Assert.Contains("{Binding Server.Loader}", attributeValues);
        Assert.Contains("{Binding Server.OnlinePlayers}", attributeValues);
        Assert.Contains("{Binding Server.MaxPlayers}", attributeValues);
        Assert.Contains("{Binding AccessText}", attributeValues);
        Assert.Contains("{Binding ClientStateText}", attributeValues);
        Assert.Contains("{Binding ClientActionText}", attributeValues);
        Assert.Contains("{Binding ClientActionIcon}", attributeValues);
        Assert.Contains("{Binding ClientActionHint}", attributeValues);

        var prepareButton = template
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value.Contains(
                    "PrepareActivityClientCommand",
                    StringComparison.Ordinal) == true);
        Assert.Equal("6", prepareButton.Attribute("Grid.Row")?.Value);
        Assert.Contains(
            prepareButton.Descendants(),
            element => element.Name.LocalName == "IconParkIcon");

        var statusDotStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ActivityStatusDotStyle");
        Assert.Contains(
            statusDotStyle.Descendants(presentation + "DataTrigger"),
            trigger =>
                trigger.Attribute("Binding")?.Value ==
                    "{Binding Server.Status}");
    }

    [Fact]
    public void ServerHome_ExposesActivityScheduleInCatalogAndSelectedDetails()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var serverList = document
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute("ItemsSource")?.Value ==
                "{Binding Servers}");
        Assert.Contains(
            serverList.Descendants(presentation + "Setter"),
            element =>
                element.Attribute("Property")?.Value ==
                    "AutomationProperties.HelpText" &&
                element.Attribute("Value")?.Value ==
                    "{Binding Converter={StaticResource ServerListMetaTextConverter}}");

        var selectedSchedule = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value ==
                "{Binding SelectedServerScheduleText}");
        var schedulePanel = selectedSchedule
            .Ancestors(presentation + "Border")
            .First(element => element.Attribute("Visibility") is not null);
        Assert.Equal(
            "{Binding HasSelectedServerSchedule, Converter={StaticResource BooleanToVisibilityConverter}}",
            schedulePanel.Attribute("Visibility")?.Value);
        Assert.Contains(
            schedulePanel.Descendants(),
            element =>
                element.Name.LocalName == "IconParkIcon" &&
                element.Attribute("Kind")?.Value == "Calendar");
    }

    [Fact]
    public void ServerHome_UsesCatalogInspectorStructureWithLiveQuickSettings()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var navigationColumn = document
            .Descendants(presentation + "ColumnDefinition")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "NavigationColumn");
        var directoryColumn = document
            .Descendants(presentation + "ColumnDefinition")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "DirectoryColumn");
        var directoryPanel = document
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ServerDirectoryPanel");
        var serverList = document
            .Descendants(presentation + "ListBox")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding Servers}");
        var titleBar = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "TitleBar");
        var serverWorkspace = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "ServerWorkspace");
        var heroDetails = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerHeroDetails");
        var heroActions = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerHeroActions");
        var primaryActionButton = heroActions
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "SelectedServerPrimaryActionButton");
        var quickSettingFields = document
            .Descendants(presentation + "StackPanel")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeQuickSettingFields");
        var quickSettingsGrid = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeQuickSettingsGrid");
        var quickSettingsPanel = document
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeQuickSettingsPanel");
        var serverListTemplateRoot = document
            .Descendants(presentation + "DataTemplate")
            .Single(element =>
                element.Attribute("DataType")?.Value ==
                "{x:Type contracts:ServerSummary}" &&
                element.Ancestors(presentation + "ListBox").Any(list =>
                    list.Attribute("ItemsSource")?.Value ==
                    "{Binding Servers}"))
            .Element(presentation + "Grid")!;
        var memorySelector = document
            .Descendants(presentation + "ComboBox")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "HomeMemorySelector");
        var chooseJavaButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("AutomationProperties.Name")?.Value ==
                "更改当前档案 Java");
        var catalogItemsPanel = serverList
            .Descendants(presentation + "ItemsPanelTemplate")
            .Descendants(presentation + "WrapPanel")
            .Single();

        Assert.Equal("0", navigationColumn.Attribute("Width")?.Value);
        Assert.Equal("*", directoryColumn.Attribute("Width")?.Value);
        Assert.Equal("1", directoryPanel.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", directoryPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            "{DynamicResource DirectoryBrush}",
            directoryPanel.Attribute("Background")?.Value);
        Assert.Equal("0,0,1,0", directoryPanel.Attribute("BorderThickness")?.Value);
        Assert.Null(serverList.Attribute("MaxHeight"));
        Assert.Equal("28,8,14,14", serverList.Attribute("Margin")?.Value);
        Assert.Equal("True", catalogItemsPanel.Attribute("IsItemsHost")?.Value);
        Assert.Equal("0", titleBar.Attribute("Grid.Column")?.Value);
        Assert.Equal("3", titleBar.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("0", titleBar.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", serverWorkspace.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", serverWorkspace.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            new[] { "56", "*", "76" },
            serverWorkspace
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value));
        Assert.Equal(
            new[] { "Auto", "Auto", "Auto" },
            heroDetails
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value));
        Assert.Equal(
            new[] { "112", "*" },
            serverListTemplateRoot
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value));
        Assert.Equal(
            "{Binding SelectedMemory, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            memorySelector.Attribute("SelectedItem")?.Value);
        Assert.Equal(
            "ChooseProfileJavaButton_OnClick",
            chooseJavaButton.Attribute("Click")?.Value);
        Assert.Equal(
            "{StaticResource MemoryComboBoxStyle}",
            memorySelector.Attribute("Style")?.Value);
        Assert.Equal("46", primaryActionButton.Attribute("Height")?.Value);
        Assert.Equal("46", primaryActionButton.Attribute("MinHeight")?.Value);
        Assert.Equal("22,15", primaryActionButton.Attribute("Margin")?.Value);
        Assert.Equal("18,8", primaryActionButton.Attribute("Padding")?.Value);
        Assert.Equal("2", heroActions.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            new[] { "*" },
            heroActions
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .Select(element => element.Attribute("Width")?.Value));
        Assert.Equal("0,22,0,0", quickSettingsPanel.Attribute("Margin")?.Value);
        Assert.Equal("Transparent", quickSettingsPanel.Attribute("Background")?.Value);
        Assert.Equal("1", quickSettingFields.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            new[] { "Auto", "Auto" },
            quickSettingsGrid
                .Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(element => element.Attribute("Height")?.Value));
        foreach (var fieldName in new[]
                 {
                     "HomeJavaQuickSetting",
                     "HomeMemoryQuickSetting",
                     "HomeDirectoryQuickSetting"
                 })
        {
            var field = document
                .Descendants(presentation + "Border")
                .Single(element =>
                    element.Attribute(x + "Name")?.Value == fieldName);
            Assert.Equal("1", field.Attribute("BorderThickness")?.Value);
            Assert.Equal(
                "{DynamicResource SurfaceMutedBrush}",
                field.Attribute("Background")?.Value);
        }
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute(x + "Name")?.Value is
                "NavigationRail" or "NavigationAccountPanel" or
                "SelectedServerRow" or "HomeHighlightsRow");
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding SelectedProfileGameDirectoryDisplayText}");

        var theme = LoadThemeXaml();
        var navigationStyle = theme
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "WorkspaceNavigationToggleStyle");
        var serverItemStyle = theme
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ServerCardListItemStyle");
        Assert.Contains(
            navigationStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Height" &&
                setter.Attribute("Value")?.Value == "42");
        Assert.Contains(
            serverItemStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Width" &&
                setter.Attribute("Value")?.Value == "342");
        Assert.Contains(
            serverItemStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Height" &&
                setter.Attribute("Value")?.Value == "204");
    }

    [Fact]
    public void MemoryComboBox_UsesCrispPopupSurfaceAndVisibleItemFocus()
    {
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var comboBoxStyle = document
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "MemoryComboBoxStyle");
        var itemStyle = comboBoxStyle
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == "ComboBoxItem");
        var popupSurface = comboBoxStyle
            .Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute("MinWidth")?.Value ==
                "{TemplateBinding ActualWidth}");

        Assert.Contains(
            itemStyle.Elements(presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                setter.Attribute("Value")?.Value ==
                "{StaticResource HechaoFocusVisualStyle}");
        Assert.Equal(
            "{DynamicResource BorderStrongBrush}",
            popupSurface.Attribute("BorderBrush")?.Value);
        Assert.Equal("0,4,0,0", popupSurface.Attribute("Margin")?.Value);
        Assert.Empty(
            popupSurface.Descendants(presentation + "DropShadowEffect"));
    }

    [Fact]
    public void RegistrationAndNotifications_ExposeExplicitConsentAndCompactChrome()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var consent = document
            .Descendants(presentation + "CheckBox")
            .Single(element =>
                element.Attribute("IsChecked")?.Value.Contains(
                    "IsRegistrationLegalAccepted",
                    StringComparison.Ordinal) == true);
        Assert.Contains(
            consent.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains(
                "用户协议",
                StringComparison.Ordinal) == true);

        var registerButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("AutomationProperties.Name")?.Value == "创建赫朝账号");
        Assert.Equal(
            "{Binding CanSubmitRegistrationForm}",
            registerButton.Attribute("IsEnabled")?.Value);

        var notificationToggle = document
            .Descendants(presentation + "ToggleButton")
            .Single(element => element
                .Descendants()
                .Any(descendant =>
                    descendant.Name.LocalName == "IconParkIcon" &&
                    descendant.Attribute("Kind")?.Value == "Remind"));
        Assert.Equal("38", notificationToggle.Attribute("Width")?.Value);
        Assert.Equal("38", notificationToggle.Attribute("Height")?.Value);
        Assert.Equal("42", notificationToggle.Parent?.Attribute("Width")?.Value);
        Assert.Equal("66", notificationToggle.Parent?.Attribute("Height")?.Value);
        Assert.Null(notificationToggle.Attribute("Command"));

        var notificationPanel = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value == "NotificationsPanel");
        var shadow = notificationPanel
            .Descendants(presentation + "DropShadowEffect")
            .Single();
        Assert.Equal(
            "{DynamicResource PanelShadowColor}",
            shadow.Attribute("Color")?.Value);
        Assert.Equal("12", shadow.Attribute("BlurRadius")?.Value);
        Assert.Equal("3", shadow.Attribute("ShadowDepth")?.Value);
    }

    [Fact]
    public void ActivityPage_UsesSixWeekCalendarAndIconParkMonthNavigation()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var calendarDays = document
            .Descendants(presentation + "ItemsControl")
            .Single(element =>
                element.Attribute(x + "Name")?.Value ==
                "ActivityCalendarDays");
        var dayGrid = calendarDays
            .Element(presentation + "ItemsControl.ItemsPanel")!
            .Descendants(presentation + "UniformGrid")
            .Single();
        var selectDayButton = calendarDays
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value?.Contains(
                    "SelectDayCommand",
                    StringComparison.Ordinal) == true);

        Assert.Equal(
            "{Binding ActivityCalendar.Days}",
            calendarDays.Attribute("ItemsSource")?.Value);
        Assert.DoesNotContain(
            calendarDays.AncestorsAndSelf(),
            element => element.Attribute("Visibility")?.Value.Contains(
                "HasActivityServers",
                StringComparison.Ordinal) == true);
        Assert.Equal("7", dayGrid.Attribute("Columns")?.Value);
        Assert.Equal(
            "{Binding}",
            selectDayButton.Attribute("CommandParameter")?.Value);

        var navigation = new Dictionary<string, string>
        {
            ["{Binding ActivityCalendar.PreviousMonthCommand}"] = "Left",
            ["{Binding ActivityCalendar.GoToTodayCommand}"] = "Calendar",
            ["{Binding ActivityCalendar.NextMonthCommand}"] = "Right",
        };
        foreach (var (command, iconKind) in navigation)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute("Command")?.Value == command);
            Assert.Contains(
                button.Descendants(),
                element =>
                    element.Name.LocalName == "IconParkIcon" &&
                    element.Attribute("Kind")?.Value == iconKind);
        }

        Assert.Contains(
            document.Descendants(presentation + "ListBox"),
            element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding ActivityCalendar.SelectedActivities}");
        Assert.Contains(
            document.Descendants(presentation + "ListBox"),
            element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding ActivityCalendar.UnscheduledActivities}");
    }

    [Fact]
    public void InkFaintColor_MeetsNormalTextContrastOnThemeSurfaces()
    {
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var backgroundKeys = new[]
        {
            "CanvasColor",
            "SurfaceColor",
            "SurfaceMutedColor",
            "SurfacePressedColor",
            "RailColor",
            "DirectoryColor",
            "StripColor",
        };

        foreach (var paletteName in new[] { "DarkPalette.xaml", "LightPalette.xaml" })
        {
            var colors = LoadPaletteXaml(paletteName)
                .Descendants(presentation + "Color")
                .ToDictionary(
                    element => element.Attribute(x + "Key")!.Value,
                    element => element.Value);
            Assert.All(
                backgroundKeys,
                key => Assert.True(
                    ContrastRatio(colors["InkFaintColor"], colors[key]) >= 4.5,
                    $"{paletteName}: InkFaintColor contrast against {key} must be at least 4.5:1."));
        }
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

    private static XDocument LoadPaletteXaml(string paletteName)
    {
        var repositoryRoot = FindRepositoryRoot();
        return XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Launcher",
            "Themes",
            paletteName));
    }

    private static string? GetStaticResourceKey(string? value)
    {
        const string prefix = "{StaticResource ";
        return value is not null &&
               value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.EndsWith('}')
            ? value[prefix.Length..^1]
            : null;
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var hex = color.Trim().TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        var channels = Enumerable.Range(0, 3)
            .Select(index =>
                Convert.ToInt32(hex.Substring(index * 2, 2), 16) / 255d)
            .Select(channel =>
                channel <= 0.04045
                    ? channel / 12.92
                    : Math.Pow((channel + 0.055) / 1.055, 2.4))
            .ToArray();
        return
            (0.2126 * channels[0]) +
            (0.7152 * channels[1]) +
            (0.0722 * channels[2]);
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
