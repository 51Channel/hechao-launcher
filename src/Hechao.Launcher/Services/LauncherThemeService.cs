using System.Windows;
using System.Windows.Media;

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

        var replacement = _paletteFactory(
            new Uri(requestedSource, UriKind.Relative));
        var brushUpdates = CreateThemeBrushUpdates(replacement);

        if (paletteIndexes.Length == 0)
        {
            replacement[PaletteSourceMarker] = requestedSource;
            mergedDictionaries.Insert(0, replacement);
            ApplyThemeBrushUpdates(brushUpdates);
            return;
        }

        // Keep the active dictionary instance alive. Replacing it can leave
        // already materialized Freezables bound to colors from the old palette.
        var activePalette = mergedDictionaries[paletteIndexes[0]];
        var replacementKeys = replacement.Keys
            .Cast<object>()
            .Where(key => !Equals(key, PaletteSourceMarker))
            .ToHashSet();

        foreach (var key in activePalette.Keys
                     .Cast<object>()
                     .Where(key => !Equals(key, PaletteSourceMarker))
                     .ToArray())
        {
            if (!replacementKeys.Contains(key))
            {
                activePalette.Remove(key);
            }
        }

        foreach (var key in replacementKeys)
        {
            activePalette[key] = replacement[key];
        }

        ApplyThemeBrushUpdates(brushUpdates);
        activePalette[PaletteSourceMarker] = requestedSource;

        for (var index = paletteIndexes.Length - 1; index >= 1; index--)
        {
            mergedDictionaries.RemoveAt(paletteIndexes[index]);
        }
    }

    private (SolidColorBrush Brush, Color Color)[] CreateThemeBrushUpdates(
        ResourceDictionary palette)
    {
        var updates = new List<(SolidColorBrush Brush, Color Color)>();
        foreach (var key in palette.Keys.Cast<object>())
        {
            if (key is not string colorKey ||
                palette[key] is not Color color ||
                !colorKey.EndsWith("Color", StringComparison.Ordinal))
            {
                continue;
            }

            var brushKey = $"{colorKey[..^"Color".Length]}Brush";
            if (FindBrush(_applicationResources, brushKey) is not { } brush)
            {
                continue;
            }

            if (brush.IsFrozen)
            {
                throw new InvalidOperationException(
                    $"主题画刷 {brushKey} 已被冻结，无法在运行时切换主题。");
            }

            updates.Add((brush, color));
        }

        return updates.ToArray();
    }

    private static void ApplyThemeBrushUpdates(
        IEnumerable<(SolidColorBrush Brush, Color Color)> updates)
    {
        foreach (var (brush, color) in updates)
        {
            // Preserve the DynamicResource expression so a light-theme cold
            // start cannot make WPF freeze this brush before the next switch.
            brush.SetCurrentValue(SolidColorBrush.ColorProperty, color);
        }
    }

    private static SolidColorBrush? FindBrush(
        ResourceDictionary dictionary,
        string key)
    {
        if (dictionary.Contains(key) && dictionary[key] is SolidColorBrush brush)
        {
            return brush;
        }

        for (var index = dictionary.MergedDictionaries.Count - 1;
             index >= 0;
             index--)
        {
            if (FindBrush(dictionary.MergedDictionaries[index], key) is { } mergedBrush)
            {
                return mergedBrush;
            }
        }

        return null;
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
