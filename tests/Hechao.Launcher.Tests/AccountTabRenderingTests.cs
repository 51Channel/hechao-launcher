using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hechao.Launcher.Tests;

public sealed class AccountTabRenderingTests
{
    [Fact]
    public void SelectedAccountTab_RendersItsRightBorderInsideTheLayoutSlot()
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var rightBorderPixelCount = 0;
        var totalRedPixelCount = 0;
        var redMinimumX = int.MaxValue;
        var redMaximumX = int.MinValue;
        var redMinimumY = int.MaxValue;
        var redMaximumY = int.MinValue;
        var selectedOriginX = -1d;
        var selectedWidth = -1d;
        var sampledRightEdge = -1;
        var edgeRightInset = double.MaxValue;
        var rightSideRedPixels = string.Empty;
        var edgeDiagnostics = string.Empty;

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var resources = LoadThemeResources();
                var tabStyle = (Style)resources["AccountTabStyle"];
                var selectedTab = new TabItem
                {
                    Header = "登录",
                    IsSelected = true,
                    Style = tabStyle
                };
                var tabControl = new TabControl
                {
                    Width = 320,
                    Height = 160,
                    SelectedIndex = 0,
                    Background = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Items =
                    {
                        selectedTab,
                        new TabItem
                        {
                            Header = "注册",
                            Style = tabStyle
                        }
                    }
                };
                var window = new Window
                {
                    Width = 340,
                    Height = 180,
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = tabControl
                };
                window.Resources.MergedDictionaries.Add(resources);

                window.ContentRendered += (_, _) =>
                {
                    var edge = Assert.IsType<System.Windows.Shapes.Rectangle>(
                        selectedTab.Template.FindName(
                            "TabRightEdge",
                            selectedTab));
                    var edgeOrigin = edge.TranslatePoint(
                        new Point(0, 0),
                        tabControl);
                    edgeDiagnostics =
                        $"edge X/Y/width/height/background: {edgeOrigin.X}/" +
                        $"{edgeOrigin.Y}/{edge.ActualWidth}/{edge.ActualHeight}/" +
                        $"{edge.Fill}";
                    var width = (int)Math.Ceiling(tabControl.ActualWidth);
                    var height = (int)Math.Ceiling(tabControl.ActualHeight);
                    var bitmap = new RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(tabControl);

                    var origin = selectedTab.TranslatePoint(
                        new Point(0, 0),
                        tabControl);
                    selectedOriginX = origin.X;
                    selectedWidth = selectedTab.ActualWidth;
                    var rightEdge = (int)Math.Round(
                        origin.X + selectedTab.ActualWidth);
                    sampledRightEdge = rightEdge;
                    edgeRightInset =
                        origin.X + selectedTab.ActualWidth -
                        edgeOrigin.X -
                        edge.ActualWidth;
                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);

                    for (var x = 0; x < width; x++)
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var offset = ((y * width) + x) * 4;
                            var blue = pixels[offset];
                            var green = pixels[offset + 1];
                            var red = pixels[offset + 2];
                            if (red >= 150 &&
                                red >= green + 60 &&
                                red >= blue + 60)
                            {
                                totalRedPixelCount++;
                                redMinimumX = Math.Min(redMinimumX, x);
                                redMaximumX = Math.Max(redMaximumX, x);
                                redMinimumY = Math.Min(redMinimumY, y);
                                redMaximumY = Math.Max(redMaximumY, y);
                                if (x >= rightEdge - 12 &&
                                    rightSideRedPixels.Length < 240)
                                {
                                    rightSideRedPixels += $"({x},{y})";
                                }
                            }
                        }
                    }

                    for (var x = Math.Max(0, redMaximumX - 15);
                         x <= Math.Min(width - 1, redMaximumX);
                         x++)
                    {
                        for (var y = Math.Max(0, redMinimumY + 3);
                             y < Math.Min(height, redMaximumY - 3);
                             y++)
                        {
                            var offset = ((y * width) + x) * 4;
                            var blue = pixels[offset];
                            var green = pixels[offset + 1];
                            var red = pixels[offset + 2];
                            if (red >= 150 &&
                                red >= green + 60 &&
                                red >= blue + 60)
                            {
                                rightBorderPixelCount++;
                            }
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

        Assert.True(
            rightBorderPixelCount >= 8,
            "Expected a visible selected-tab right border, but found only " +
            $"{rightBorderPixelCount} red pixels. Total red pixels: " +
            $"{totalRedPixelCount}; red X range: {redMinimumX}..{redMaximumX}; " +
            $"selected tab X/width/right: {selectedOriginX}/{selectedWidth}/" +
            $"{sampledRightEdge}; {edgeDiagnostics}; nearby red pixels: " +
            $"{rightSideRedPixels}.");
        Assert.InRange(edgeRightInset, 0d, 16d);
    }

    private static ResourceDictionary LoadThemeResources()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        var themePath = Path.Combine(
            current.FullName,
            "src",
            "Hechao.Launcher",
            "Themes",
            "HechaoTheme.xaml");
        var xaml = File.ReadAllText(themePath).Replace(
            "clr-namespace:Hechao.Launcher.Controls",
            "clr-namespace:Hechao.Launcher.Controls;assembly=Hechao.Launcher",
            StringComparison.Ordinal);
        return Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));
    }
}
