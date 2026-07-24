using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hechao.Launcher.Controls;

public enum IconParkKind
{
    None,
    Announcement,
    Box,
    Calendar,
    CheckOne,
    Close,
    Code,
    Delete,
    Down,
    Download,
    ExternalTransmission,
    Folder,
    FolderOpen,
    FullScreen,
    Game,
    History,
    IdCard,
    Install,
    Lock,
    Minus,
    PlayOne,
    Refresh,
    Remind,
    Repair,
    Right,
    Server,
    SettingTwo,
    Shield,
    Tool,
    Up,
    User,
    VolumeNotice,
}

public sealed class IconParkIcon : FrameworkElement
{
    private const double ViewBoxSize = 48d;
    private const double DefaultIconSize = 18d;

    private static readonly IReadOnlyDictionary<string, IconParkKind> LegacyGlyphMap =
        new Dictionary<string, IconParkKind>(StringComparer.Ordinal)
        {
            ["\uE968"] = IconParkKind.Server,
            ["\uE896"] = IconParkKind.Download,
            ["\uE787"] = IconParkKind.Calendar,
            ["\uE77B"] = IconParkKind.User,
            ["\uE713"] = IconParkKind.SettingTwo,
            ["\uE72C"] = IconParkKind.Refresh,
            ["\uE76C"] = IconParkKind.Right,
            ["\uE7F4"] = IconParkKind.Remind,
            ["\uE921"] = IconParkKind.Minus,
            ["\uE922"] = IconParkKind.FullScreen,
            ["\uE8BB"] = IconParkKind.Close,
            ["\uE7B8"] = IconParkKind.Box,
            ["\uE789"] = IconParkKind.VolumeNotice,
            ["\uE8A7"] = IconParkKind.Right,
            ["\uE838"] = IconParkKind.FolderOpen,
            ["\uE943"] = IconParkKind.Code,
            ["\uE8B7"] = IconParkKind.Folder,
            ["\uE90F"] = IconParkKind.Tool,
            ["\uE74D"] = IconParkKind.Delete,
            ["\uE72E"] = IconParkKind.ExternalTransmission,
            ["\uE774"] = IconParkKind.Shield,
            ["\uE785"] = IconParkKind.Lock,
            ["\uE711"] = IconParkKind.Close,
            ["\uE895"] = IconParkKind.CheckOne,
            ["\uE73E"] = IconParkKind.CheckOne,
            ["\uE768"] = IconParkKind.PlayOne,
            ["\uE70D"] = IconParkKind.Down,
            ["\uE70E"] = IconParkKind.Up,
        };

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(IconParkKind),
            typeof(IconParkIcon),
            new FrameworkPropertyMetadata(
                IconParkKind.None,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(IconParkIcon),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(IconParkIcon),
            new FrameworkPropertyMetadata(
                Brushes.Black,
                FrameworkPropertyMetadataOptions.Inherits |
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IconParkIcon()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public IconParkKind Kind
    {
        get => (IconParkKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? DefaultIconSize : Width;
        var height = double.IsNaN(Height) ? DefaultIconSize : Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var resolvedKind = ResolveKind();
        var parts = IconParkGeometryRegistry.GetParts(resolvedKind);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (parts.Count == 0 || size <= 0)
        {
            return;
        }

        var scale = size / ViewBoxSize;
        var offsetX = (ActualWidth - size) / 2d;
        var offsetY = (ActualHeight - size) / 2d;
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));

        drawingContext.PushTransform(transform);
        foreach (var part in parts)
        {
            Pen? pen = null;
            if (part.Stroke)
            {
                pen = new Pen(Foreground, part.StrokeWidth)
                {
                    StartLineCap = part.LineCap,
                    EndLineCap = part.LineCap,
                    LineJoin = part.LineJoin,
                };
            }

            drawingContext.DrawGeometry(
                part.Fill ? Foreground : null,
                pen,
                part.Geometry);
        }

        drawingContext.Pop();
    }

    private IconParkKind ResolveKind()
    {
        if (Kind != IconParkKind.None)
        {
            return Kind;
        }

        return Glyph is not null && LegacyGlyphMap.TryGetValue(Glyph, out var mappedKind)
            ? mappedKind
            : IconParkKind.None;
    }
}
