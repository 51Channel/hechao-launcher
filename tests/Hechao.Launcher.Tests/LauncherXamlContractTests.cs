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
    public void RailNavigation_IsTwoWayAndHasStableAutomationNames()
    {
        var launcher = LoadLauncherXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageBindings = new[]
        {
            "IsServersPage",
            "IsDownloadsPage",
            "IsActivitiesPage",
            "IsAccountPage",
            "IsSettingsPage"
        };

        foreach (var pageBinding in pageBindings)
        {
            var toggle = launcher
                .Descendants(presentation + "ToggleButton")
                .Single(element =>
                    element.Attribute("IsChecked")?.Value.Contains(
                        pageBinding,
                        StringComparison.Ordinal) == true);

            Assert.Contains("Mode=TwoWay", toggle.Attribute("IsChecked")!.Value);
            Assert.False(string.IsNullOrWhiteSpace(
                toggle.Attribute("AutomationProperties.Name")?.Value));
        }
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

        Assert.Equal(3, progressBars.Length);
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
        var buttons = actionsPanel.Elements(presentation + "Button").ToArray();
        Assert.Equal(4, buttons.Length);
        Assert.Equal(
            ["回滚", "修复", "删除", "设置"],
            buttons.Select(button =>
                button.Descendants(presentation + "TextBlock")
                    .Single()
                    .Attribute("Text")?.Value));
        Assert.All(
            buttons,
            button => Assert.False(string.IsNullOrWhiteSpace(
                button.Attribute("ToolTip")?.Value)));
    }

    [Fact]
    public void ServerDetails_UsesAutomaticVerticalScrollbar()
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
            "Auto",
            scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
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
    public void SidebarAccountAvatar_UsesMinecraftSkinHeadAndHatLayers()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var avatar = document
            .Descendants()
            .Single(element =>
                element.Attribute(x + "Name")?.Value == "SidebarAccountAvatar");
        var brushes = avatar
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

    [Fact]
    public void ServerDetails_LongPathValuesUseEllipsisAndTooltip()
    {
        var launcher = LoadLauncherXaml();
        var theme = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var valueStyle = theme
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value == "DetailRowValueStyle");
        var setters = valueStyle
            .Elements(presentation + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")?.Value);
        var gameDirectory = launcher
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute("Text")?.Value ==
                "{Binding SelectedProfileGameDirectory}");

        Assert.Equal("Stretch", setters["HorizontalAlignment"]);
        Assert.Equal("Right", setters["TextAlignment"]);
        Assert.Equal("CharacterEllipsis", setters["TextTrimming"]);
        Assert.Equal(
            "{Binding SelectedProfileGameDirectory}",
            gameDirectory.Attribute("ToolTip")?.Value);
    }

    [Fact]
    public void Settings_DeclaresDiagnosticsRowAndGuardsDirectoryChanges()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var diagnosticsHeading = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "故障诊断");
        var diagnosticsPanel = diagnosticsHeading
            .Ancestors(presentation + "Border")
            .First();
        var settingsGrid = Assert.IsType<XElement>(diagnosticsPanel.Parent);
        var rowDefinitions = settingsGrid
            .Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .ToArray();
        var changeDirectoryButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Content")?.Value == "更改目录");

        Assert.Equal("4", diagnosticsPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal(5, rowDefinitions.Length);
        Assert.Equal(
            "{Binding CanChangeClientDirectory}",
            changeDirectoryButton.Attribute("IsEnabled")?.Value);
    }

    [Fact]
    public void SettingsSwitches_HaveAccessibleNamesAndAccurateCacheCopy()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var switches = document
            .Descendants(presentation + "ToggleButton")
            .Where(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource SwitchToggleStyle}")
            .ToArray();
        var cacheLabels = document
            .Descendants(presentation + "TextBlock")
            .Where(element =>
                element.Attribute("Text")?.Value ==
                "保留已下载文件缓存")
            .ToArray();

        Assert.Equal(8, switches.Length);
        Assert.All(
            switches,
            element => Assert.False(string.IsNullOrWhiteSpace(
                element.Attribute("AutomationProperties.Name")?.Value)));
        Assert.Equal(2, cacheLabels.Length);
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value ==
                "保留校验通过的下载文件，供后续安装、更新或修复复用。");
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
    public void StatusToastAndProgressIndicators_UseDynamicBindings()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var playerText = document
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute("Text")?.Value ==
                "{Binding SelectedServerPlayerText}");
        var statusDot = playerText
            .Parent!
            .Elements(presentation + "Ellipse")
            .Single();
        var statusValues = statusDot
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
            "{x:Static contracts:ServerStatus.Closed}",
            statusValues);
        Assert.Contains(
            statusDot.Descendants(presentation + "Setter"),
            element =>
                element.Attribute("Property")?.Value == "Fill" &&
                element.Attribute("Value")?.Value ==
                "{StaticResource ClosedBrush}");

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

        var progressSteps = document
            .Descendants(presentation + "Ellipse")
            .Where(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource ProgressStepIndicatorStyle}")
            .ToArray();
        Assert.Equal(4, progressSteps.Length);
        Assert.Equal(
            [
                "{Binding ProgressStepOneState}",
                "{Binding ProgressStepTwoState}",
                "{Binding ProgressStepThreeState}",
                "{Binding ProgressStepFourState}",
            ],
            progressSteps.Select(element => element.Attribute("Tag")?.Value));

        var progressGlyphs = document
            .Descendants(presentation + "TextBlock")
            .Where(element =>
                element.Attribute("Style")?.Value ==
                "{StaticResource ProgressStepGlyphStyle}")
            .ToArray();
        Assert.Equal(4, progressGlyphs.Length);
        Assert.Equal(
            ["检查文件", "下载资源", "应用更新", "准备运行"],
            progressGlyphs.Select(element =>
                element.Attribute("AutomationProperties.Name")?.Value));
        Assert.Equal(
            [
                "{Binding ProgressStepOneStatusText}",
                "{Binding ProgressStepTwoStatusText}",
                "{Binding ProgressStepThreeStatusText}",
                "{Binding ProgressStepFourStatusText}",
            ],
            progressGlyphs.Select(element =>
                element.Attribute("AutomationProperties.ItemStatus")?.Value));

        var theme = LoadThemeXaml();
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var glyphStyle = theme
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(x + "Key")?.Value ==
                "ProgressStepGlyphStyle");
        var glyphTriggerValues = glyphStyle
            .Descendants(presentation + "Trigger")
            .Select(element => element.Attribute("Value")?.Value)
            .ToArray();
        Assert.Contains(
            "{x:Static viewModels:ProgressStepState.Current}",
            glyphTriggerValues);
        Assert.Contains(
            "{x:Static viewModels:ProgressStepState.Complete}",
            glyphTriggerValues);
        Assert.Contains(
            "{x:Static viewModels:ProgressStepState.Failed}",
            glyphTriggerValues);
        var glyphValues = glyphStyle
            .Descendants(presentation + "Setter")
            .Where(element => element.Attribute("Property")?.Value == "Text")
            .Select(element => element.Attribute("Value")?.Value)
            .ToArray();
        Assert.Contains("•", glyphValues);
        Assert.Contains("✓", glyphValues);
        Assert.Contains("×", glyphValues);
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
        Assert.Equal("5", prepareButton.Attribute("Grid.Row")?.Value);
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
    public void ServerHome_ShowsActivityScheduleInListAndSelectedDetails()
    {
        var document = LoadLauncherXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding Converter={StaticResource ServerListMetaTextConverter}}");

        var selectedSchedule = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value ==
                "{Binding SelectedServerScheduleText}");
        var schedulePanel = Assert.IsType<XElement>(selectedSchedule.Parent);
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
    public void ServerHome_UsesReferenceStructureWithLiveContentAndQuickSettings()
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
        var announcementList = document
            .Descendants(presentation + "ItemsControl")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding HomeAnnouncementServers}");
        var upcomingList = document
            .Descendants(presentation + "ItemsControl")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding ActivityCalendar.UpcomingActivities}");
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

        Assert.Equal("168", navigationColumn.Attribute("Width")?.Value);
        Assert.Equal(
            "{Binding SelectedMemory, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            memorySelector.Attribute("SelectedItem")?.Value);
        Assert.Equal(
            "ChooseProfileJavaButton_OnClick",
            chooseJavaButton.Attribute("Click")?.Value);
        Assert.Equal(
            "{Binding HasNoHomeAnnouncements, Converter={StaticResource BooleanToVisibilityConverter}}",
            announcementList.Parent!
                .Elements(presentation + "StackPanel")
                .Single()
                .Attribute("Visibility")?.Value);
        Assert.Equal(
            "{Binding ActivityCalendar.HasNoUpcomingActivities, Converter={StaticResource BooleanToVisibilityConverter}}",
            upcomingList.Parent!
                .Elements(presentation + "StackPanel")
                .Single()
                .Attribute("Visibility")?.Value);
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding ClientDirectory}");
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
        var document = LoadThemeXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = document
            .Descendants(presentation + "Color")
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Value);
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

        Assert.All(
            backgroundKeys,
            key => Assert.True(
                ContrastRatio(colors["InkFaintColor"], colors[key]) >= 4.5,
                $"InkFaintColor contrast against {key} must be at least 4.5:1."));
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
