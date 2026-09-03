using System.Windows;
using System.Windows.Media;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherThemeServiceTests
{
    [Fact]
    public void Apply_ReplacesOnlyThePaletteAtItsExistingIndex()
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
        Assert.Equal(Color.FromRgb(0x0C, 0x0D, 0x0F), darkCanvas);
    }

    private static ResourceDictionary CreatePalette(string source)
    {
        var palette = new ResourceDictionary();
        palette[LauncherThemeService.PaletteSourceMarker] = source;
        return palette;
    }
}
