using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Hechao.Launcher.Tests;

public sealed class AccountTabRenderingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SelectedAccountTab_RendersACompleteBorderAtItsOuterEdge(
        int selectedIndex)
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var selectedTabWidth = -1d;
        var otherTabWidth = -1d;
        var borderWidth = -1d;
        var physicalBorderWidth = -1d;
        var selectedContentWidth = -1d;
        var borderLeftInset = double.MaxValue;
        var borderRightInset = double.MaxValue;
        var redMinimumX = int.MaxValue;
        var redMaximumX = int.MinValue;
        var redMinimumY = int.MaxValue;
        var redMaximumY = int.MinValue;
        var verticalBorderMinimumX = int.MaxValue;
        var verticalBorderMaximumX = int.MinValue;
        var selectedState = false;
        var renderedBorderColor = Colors.Transparent;

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var (palette, theme) = LoadThemeResources();
                var window = new Window
                {
                    Width = 340,
                    Height = 180,
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.White
                };
                window.Resources.MergedDictionaries.Add(palette);
                window.Resources.MergedDictionaries.Add(theme);

                var tabStyle = (Style)window.FindResource("AccountTabStyle");
                var tabs = new[]
                {
                    new TabItem
                    {
                        Header = "Login",
                        Content = new Border
                        {
                            MinWidth = 24,
                            Height = 24
                        },
                        Style = tabStyle
                    },
                    new TabItem
                    {
                        Header = "Register",
                        Content = new Border
                        {
                            MinWidth = 24,
                            Height = 24
                        },
                        Style = tabStyle
                    }
                };
                var tabControl = new TabControl
                {
                    Width = 320,
                    Height = 160,
                    Style = (Style)window.FindResource("AccountTabControlStyle"),
                    Items =
                    {
                        tabs[0],
                        tabs[1]
                    }
                };
                tabControl.SelectedIndex = selectedIndex;
                window.Content = tabControl;

                window.ContentRendered += (_, _) =>
                {
                    var selectedTab = tabs[selectedIndex];
                    var otherTab = tabs[1 - selectedIndex];
                    var border = Assert.IsType<Border>(
                        selectedTab.Template.FindName(
                            "TabBackground",
                            selectedTab));
                    var tabOrigin = selectedTab.TranslatePoint(
                        new Point(0, 0),
                        tabControl);
                    var borderOrigin = border.TranslatePoint(
                        new Point(0, 0),
                        tabControl);

                    selectedTabWidth = selectedTab.ActualWidth;
                    otherTabWidth = otherTab.ActualWidth;
                    borderWidth = border.ActualWidth;
                    borderLeftInset = borderOrigin.X - tabOrigin.X;
                    borderRightInset =
                        tabOrigin.X + selectedTab.ActualWidth -
                        borderOrigin.X - border.ActualWidth;
                    selectedState = selectedTab.IsSelected;
                    renderedBorderColor =
                        Assert.IsType<SolidColorBrush>(border.BorderBrush).Color;

                    var dpi = VisualTreeHelper.GetDpi(tabControl);
                    physicalBorderWidth =
                        border.ActualWidth * dpi.DpiScaleX;
                    selectedContentWidth =
                        Assert.IsType<Border>(selectedTab.Content).ActualWidth;
                    var width = (int)Math.Ceiling(
                        tabControl.ActualWidth * dpi.DpiScaleX);
                    var height = (int)Math.Ceiling(
                        tabControl.ActualHeight * dpi.DpiScaleY);
                    var bitmap = new RenderTargetBitmap(
                        width,
                        height,
                        dpi.PixelsPerInchX,
                        dpi.PixelsPerInchY,
                        PixelFormats.Pbgra32);
                    bitmap.Render(tabControl);

                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);
                    for (var x = 0; x < width; x++)
                    {
                        for (var y = 0; y < height; y++)
                        {
                            if (!IsRedPixel(pixels, width, x, y))
                            {
                                continue;
                            }

                            redMinimumX = Math.Min(redMinimumX, x);
                            redMaximumX = Math.Max(redMaximumX, x);
                            redMinimumY = Math.Min(redMinimumY, y);
                            redMaximumY = Math.Max(redMaximumY, y);
                        }
                    }

                    for (var x = redMinimumX; x <= redMaximumX; x++)
                    {
                        var redPixelsInColumn = 0;
                        for (var y = redMinimumY; y <= redMaximumY; y++)
                        {
                            if (IsRedPixel(pixels, width, x, y))
                            {
                                redPixelsInColumn++;
                            }
                        }

                        if (redPixelsInColumn >= 20)
                        {
                            verticalBorderMinimumX =
                                Math.Min(verticalBorderMinimumX, x);
                            verticalBorderMaximumX =
                                Math.Max(verticalBorderMaximumX, x);
                        }
                    }

                    window.Close();
                    dispatcher.BeginInvokeShutdown(
                        DispatcherPriority.Background);
                };

                window.Show();
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(
            completed.Wait(TimeSpan.FromSeconds(10)),
            "The WPF account tab rendering test did not complete.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        Assert.Equal(selectedTabWidth, otherTabWidth, precision: 3);
        Assert.Equal(selectedTabWidth, borderWidth, precision: 3);
        Assert.Equal(320d, selectedContentWidth, precision: 3);
        Assert.InRange(Math.Abs(borderLeftInset), 0d, 0.01d);
        Assert.InRange(Math.Abs(borderRightInset), 0d, 0.01d);
        Assert.True(selectedState);
        Assert.Equal(Color.FromRgb(215, 71, 53), renderedBorderColor);
        Assert.NotEqual(int.MaxValue, verticalBorderMinimumX);
        Assert.NotEqual(int.MinValue, verticalBorderMaximumX);
        var renderedBorderWidth =
            verticalBorderMaximumX - verticalBorderMinimumX + 1;
        Assert.InRange(
            Math.Abs(renderedBorderWidth - physicalBorderWidth),
            0d,
            3d);
    }

    private static bool IsRedPixel(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return red >= 150 &&
               red >= green + 60 &&
               red >= blue + 60;
    }

    private static (ResourceDictionary Palette, ResourceDictionary Theme)
        LoadThemeResources()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        var palettePath = Path.Combine(
            current.FullName,
            "src",
            "Hechao.Launcher",
            "Themes",
            "DarkPalette.xaml");
        var themePath = Path.Combine(
            current.FullName,
            "src",
            "Hechao.Launcher",
            "Themes",
            "HechaoTheme.xaml");
        var themeXaml = File.ReadAllText(themePath).Replace(
            "clr-namespace:Hechao.Launcher.Controls",
            "clr-namespace:Hechao.Launcher.Controls;assembly=Hechao.Launcher",
            StringComparison.Ordinal);
        var paletteDocument = XDocument.Load(palettePath);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var paletteColors = paletteDocument
            .Descendants(presentation + "Color")
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Value,
                StringComparer.Ordinal);
        var themeDocument = XDocument.Parse(themeXaml);
        const string dynamicResourcePrefix = "{DynamicResource ";
        foreach (var attribute in themeDocument.Descendants().Attributes())
        {
            if (!attribute.Value.StartsWith(
                    dynamicResourcePrefix,
                    StringComparison.Ordinal) ||
                !attribute.Value.EndsWith('}'))
            {
                continue;
            }

            var resourceKey = attribute.Value[dynamicResourcePrefix.Length..^1];
            if (paletteColors.TryGetValue(resourceKey, out var color))
            {
                attribute.Value = color;
            }
        }

        return (
            Assert.IsType<ResourceDictionary>(
                XamlReader.Parse(File.ReadAllText(palettePath))),
            Assert.IsType<ResourceDictionary>(XamlReader.Parse(
                themeDocument.ToString(SaveOptions.DisableFormatting))));
    }
}
