using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherThemeServiceTests
{
    [Fact]
    public void Apply_UpdatesOnlyThePaletteAtItsExistingIndex()
    {
        var resources = new ResourceDictionary();
        var before = new ResourceDictionary();
        var lightPalette = CreatePalette("/Themes/LightPalette.xaml");
        var after = new ResourceDictionary();
        resources.MergedDictionaries.Add(before);
        resources.MergedDictionaries.Add(lightPalette);
        resources.MergedDictionaries.Add(after);
        var createCount = 0;
        var service = new LauncherThemeService(
            resources,
            () => { },
            source =>
            {
                createCount++;
                return CreatePalette(source.OriginalString);
            });

        service.Apply(useDarkMode: true);

        Assert.Equal(1, createCount);
        Assert.Same(before, resources.MergedDictionaries[0]);
        Assert.Same(lightPalette, resources.MergedDictionaries[1]);
        Assert.Equal(
            LauncherThemeService.DarkPaletteSource,
            LauncherThemeService.GetPaletteSource(resources.MergedDictionaries[1]));
        Assert.Same(after, resources.MergedDictionaries[2]);
    }

    [Fact]
    public void Apply_IsIdempotentWhenRequestedPaletteIsAlreadyActive()
    {
        var resources = new ResourceDictionary();
        var darkPalette = CreatePalette("/Themes/DarkPalette.xaml");
        resources.MergedDictionaries.Add(darkPalette);
        var createCount = 0;
        var service = new LauncherThemeService(
            resources,
            () => { },
            source =>
            {
                createCount++;
                return CreatePalette(source.OriginalString);
            });

        service.Apply(useDarkMode: true);
        service.Apply(useDarkMode: true);

        Assert.Equal(0, createCount);
        Assert.Single(resources.MergedDictionaries);
        Assert.Same(darkPalette, resources.MergedDictionaries[0]);
    }

    [Fact]
    public void Apply_RejectsCallsOutsideTheApplicationThread()
    {
        var resources = new ResourceDictionary();
        var lightPalette = CreatePalette(LauncherThemeService.LightPaletteSource);
        resources.MergedDictionaries.Add(lightPalette);
        var service = new LauncherThemeService(
            resources,
            () => throw new InvalidOperationException("wrong thread"),
            source => CreatePalette(source.OriginalString));

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            service.Apply(useDarkMode: true);
        });

        Assert.Equal("wrong thread", exception.Message);
        Assert.Single(resources.MergedDictionaries);
        Assert.Same(lightPalette, resources.MergedDictionaries[0]);
    }

    [Fact]
    public void Apply_LoadsThePackagedLightAndDarkPalettes()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(
            CreatePalette(LauncherThemeService.DarkPaletteSource));
        var service = new LauncherThemeService(
            resources,
            () => { },
            LauncherThemeService.LoadPalette);

        service.Apply(useDarkMode: false);
        var lightCanvas = Assert.IsType<Color>(resources["CanvasColor"]);
        service.Apply(useDarkMode: true);
        var darkCanvas = Assert.IsType<Color>(resources["CanvasColor"]);

        Assert.Equal(Color.FromRgb(0xF3, 0xF7, 0xFA), lightCanvas);
        Assert.Equal(Color.FromRgb(0x11, 0x13, 0x15), darkCanvas);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_UpdatesBrushesAlreadyUsedByTheVisualTreeAcrossRuntimeSwitches(
        bool initialUseDarkMode)
    {
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var darkPalette = LauncherThemeService.LoadPalette(
                    new Uri(LauncherThemeService.DarkPaletteSource, UriKind.Relative));
                var lightPalette = LauncherThemeService.LoadPalette(
                    new Uri(LauncherThemeService.LightPaletteSource, UriKind.Relative));
                var theme = LoadTheme();
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(
                    initialUseDarkMode ? darkPalette : lightPalette);
                resources.MergedDictionaries.Add(theme);
                var expectations = CreateThemeBrushExpectations(
                    theme,
                    darkPalette,
                    lightPalette);

                var host = new Grid
                {
                    Resources = resources
                };
                var observedBrushes = expectations.Keys.ToDictionary(
                    key => key,
                    key => Assert.IsType<SolidColorBrush>(host.FindResource(key)),
                    StringComparer.Ordinal);
                foreach (var brush in observedBrushes.Values)
                {
                    host.Children.Add(new Border { Background = brush });
                }

                var service = new LauncherThemeService(
                    resources,
                    () => { },
                    LauncherThemeService.LoadPalette);

                AssertBrushColors(observedBrushes, expectations, initialUseDarkMode);
                service.Apply(useDarkMode: !initialUseDarkMode);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                AssertBrushColors(observedBrushes, expectations, !initialUseDarkMode);
                service.Apply(useDarkMode: initialUseDarkMode);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                AssertBrushColors(observedBrushes, expectations, initialUseDarkMode);
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
            "The WPF runtime theme-switch test did not complete.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void Apply_WhenAThemeBrushIsFrozen_LeavesTheCurrentThemeUntouched()
    {
        var darkCanvas = Rgb(0x0C, 0x0D, 0x0F);
        var lightCanvas = Rgb(0xF3, 0xF7, 0xFA);
        var darkSurface = Rgb(0x17, 0x18, 0x1B);
        var lightSurface = Colors.White;
        var activePalette = CreatePalette(LauncherThemeService.DarkPaletteSource);
        activePalette["CanvasColor"] = darkCanvas;
        activePalette["SurfaceColor"] = darkSurface;
        var replacement = CreatePalette(LauncherThemeService.LightPaletteSource);
        replacement["CanvasColor"] = lightCanvas;
        replacement["SurfaceColor"] = lightSurface;

        var frozenCanvas = new SolidColorBrush(darkCanvas);
        frozenCanvas.Freeze();
        var mutableSurface = new SolidColorBrush(darkSurface);
        var theme = new ResourceDictionary
        {
            ["CanvasBrush"] = frozenCanvas,
            ["SurfaceBrush"] = mutableSurface
        };
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(activePalette);
        resources.MergedDictionaries.Add(theme);
        var service = new LauncherThemeService(
            resources,
            () => { },
            _ => replacement);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.Apply(useDarkMode: false));

        Assert.Contains("CanvasBrush", exception.Message);
        Assert.Equal(darkCanvas, Assert.IsType<Color>(activePalette["CanvasColor"]));
        Assert.Equal(darkSurface, Assert.IsType<Color>(activePalette["SurfaceColor"]));
        Assert.Equal(darkCanvas, frozenCanvas.Color);
        Assert.Equal(darkSurface, mutableSurface.Color);
        Assert.Equal(
            LauncherThemeService.DarkPaletteSource,
            LauncherThemeService.GetPaletteSource(activePalette));
    }

    private static IReadOnlyDictionary<string, (Color Dark, Color Light)>
        CreateThemeBrushExpectations(
            ResourceDictionary theme,
            ResourceDictionary darkPalette,
            ResourceDictionary lightPalette)
    {
        var expectations = theme.Keys
            .Cast<object>()
            .OfType<string>()
            .Where(key =>
                key.EndsWith("Brush", StringComparison.Ordinal) &&
                theme[key] is SolidColorBrush)
            .ToDictionary(
                key => key,
                key =>
                {
                    var colorKey = $"{key[..^"Brush".Length]}Color";
                    return (
                        Assert.IsType<Color>(darkPalette[colorKey]),
                        Assert.IsType<Color>(lightPalette[colorKey]));
                },
                StringComparer.Ordinal);

        foreach (var requiredKey in new[]
                 {
                     "CanvasBrush",
                     "RailBrush",
                     "SurfaceBrush",
                     "DirectoryBrush",
                     "StripBrush",
                     "InkBrush",
                     "BorderBrush",
                     "DividerBrush",
                     "HairlineBrush"
                 })
        {
            Assert.Contains(requiredKey, expectations.Keys);
        }

        return expectations;
    }

    private static void AssertBrushColors(
        IReadOnlyDictionary<string, SolidColorBrush> brushes,
        IReadOnlyDictionary<string, (Color Dark, Color Light)> expectations,
        bool useDarkMode)
    {
        foreach (var (key, expected) in expectations)
        {
            var brush = brushes[key];
            Assert.Equal(useDarkMode ? expected.Dark : expected.Light, brush.Color);
            Assert.True(
                DependencyPropertyHelper
                    .GetValueSource(brush, SolidColorBrush.ColorProperty)
                    .IsExpression,
                $"{key} must retain its DynamicResource color expression.");
        }
    }

    private static ResourceDictionary LoadTheme() =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri(
                "/Hechao.Launcher;component/Themes/HechaoTheme.xaml",
                UriKind.Relative)));

    private static Color Rgb(byte red, byte green, byte blue) =>
        Color.FromRgb(red, green, blue);

    private static ResourceDictionary CreatePalette(string source)
    {
        var palette = new ResourceDictionary();
        palette[LauncherThemeService.PaletteSourceMarker] = source;
        return palette;
    }
}
