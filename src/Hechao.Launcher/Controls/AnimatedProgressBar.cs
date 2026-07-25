using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Hechao.Launcher.Controls;

public sealed class AnimatedProgressBar : ProgressBar
{
    private static readonly Duration ProgressAnimationDuration =
        new(TimeSpan.FromMilliseconds(180));

    public static readonly DependencyProperty DisplayRatioProperty =
        DependencyProperty.Register(
            nameof(DisplayRatio),
            typeof(double),
            typeof(AnimatedProgressBar),
            new FrameworkPropertyMetadata(0d));

    public AnimatedProgressBar()
    {
        Loaded += (_, _) => UpdateDisplayRatio(Value, animate: false);
    }

    public double DisplayRatio
    {
        get => (double)GetValue(DisplayRatioProperty);
        private set => SetValue(DisplayRatioProperty, value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateDisplayRatio(newValue, animate: IsLoaded);
    }

    private void UpdateDisplayRatio(double value, bool animate)
    {
        var range = Maximum - Minimum;
        var target = range <= 0 || double.IsNaN(value)
            ? 0d
            : Math.Clamp((value - Minimum) / range, 0d, 1d);

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(DisplayRatioProperty, null);
            DisplayRatio = target;
            return;
        }

        var current = DisplayRatio;
        BeginAnimation(DisplayRatioProperty, null);
        DisplayRatio = current;
        BeginAnimation(
            DisplayRatioProperty,
            new DoubleAnimation(current, target, ProgressAnimationDuration)
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }
}
