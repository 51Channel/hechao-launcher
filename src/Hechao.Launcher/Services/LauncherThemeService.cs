using System.Windows;

namespace Hechao.Launcher.Services;

public interface ILauncherThemeService
{
    void Apply(bool useDarkMode);
}

public sealed class LauncherThemeService : ILauncherThemeService
{
    internal const string LightPaletteSource =
        "/Hechao.Launcher;component/Themes/LightPalette.xaml";
    internal const string DarkPaletteSource =
        "/Hechao.Launcher;component/Themes/DarkPalette.xaml";
    internal const string PaletteSourceMarker = "__HechaoLauncherPaletteSource";

    private const string LightPalettePath = "/Themes/LightPalette.xaml";
    private const string DarkPalettePath = "/Themes/DarkPalette.xaml";

    private readonly ResourceDictionary _applicationResources;
    private readonly Action _verifyAccess;
    private readonly Func<Uri, ResourceDictionary> _paletteFactory;

    public LauncherThemeService()
        : this(Application.Current ?? throw new InvalidOperationException(
            "主题服务只能在 WPF 应用启动后创建。"))
    {
    }

    public LauncherThemeService(Application application)
        : this(
            application?.Resources ?? throw new ArgumentNullException(nameof(application)),
            application.Dispatcher.VerifyAccess,
            LoadPalette)
    {
    }

    internal LauncherThemeService(
        ResourceDictionary applicationResources,
        Action verifyAccess,
        Func<Uri, ResourceDictionary> paletteFactory)
    {
        _applicationResources = applicationResources ??
            throw new ArgumentNullException(nameof(applicationResources));
        _verifyAccess = verifyAccess ?? throw new ArgumentNullException(nameof(verifyAccess));
        _paletteFactory = paletteFactory ?? throw new ArgumentNullException(nameof(paletteFactory));
    }

    public void Apply(bool useDarkMode)
    {
        _verifyAccess();

        var requestedSource = useDarkMode
            ? DarkPaletteSource
            : LightPaletteSource;
        var mergedDictionaries = _applicationResources.MergedDictionaries;
        var paletteIndexes = mergedDictionaries
            .Select((dictionary, index) => (dictionary, index))
            .Where(item => IsPaletteSource(GetPaletteSource(item.dictionary)))
            .Select(item => item.index)
            .ToArray();

        if (paletteIndexes.Length == 1 &&
            SourcesMatch(
                GetPaletteSource(mergedDictionaries[paletteIndexes[0]]),
                requestedSource))
        {
            return;
        }

        var insertionIndex = paletteIndexes.Length > 0 ? paletteIndexes[0] : 0;
        var replacement = _paletteFactory(
            new Uri(requestedSource, UriKind.Relative));
        replacement[PaletteSourceMarker] = requestedSource;

        for (var index = paletteIndexes.Length - 1; index >= 0; index--)
        {
            mergedDictionaries.RemoveAt(paletteIndexes[index]);
        }

        mergedDictionaries.Insert(
            Math.Min(insertionIndex, mergedDictionaries.Count),
            replacement);
    }

    internal static string? GetPaletteSource(ResourceDictionary dictionary)
    {
        if (dictionary.Contains(PaletteSourceMarker) &&
            dictionary[PaletteSourceMarker] is string markedSource)
        {
            return markedSource;
        }

        return dictionary.Source?.OriginalString;
    }

    internal static ResourceDictionary LoadPalette(Uri source) =>
        Application.LoadComponent(source) as ResourceDictionary ??
        throw new InvalidOperationException(
            $"主题资源 {source} 不是有效的 ResourceDictionary。");

    private static bool IsPaletteSource(string? source) =>
        IsPalettePath(source, LightPalettePath) ||
        IsPalettePath(source, DarkPalettePath);

    private static bool SourcesMatch(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ||
               (IsPalettePath(actual, LightPalettePath) &&
                IsPalettePath(expected, LightPalettePath)) ||
               (IsPalettePath(actual, DarkPalettePath) &&
                IsPalettePath(expected, DarkPalettePath));
    }

    private static bool IsPalettePath(string? source, string palettePath) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Replace('\\', '/').EndsWith(
            palettePath,
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class NullLauncherThemeService : ILauncherThemeService
{
    public static NullLauncherThemeService Instance { get; } = new();

    private NullLauncherThemeService()
    {
    }

    public void Apply(bool useDarkMode)
    {
    }
}
