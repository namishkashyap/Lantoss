using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Lantoss.UI;

/// <summary>CSS cubic-bezier timing (the design's easing curves) for WPF animations.</summary>
internal sealed class Bezier : EasingFunctionBase
{
    private readonly double x1, y1, x2, y2;
    public Bezier(double x1, double y1, double x2, double y2)
    {
        (this.x1, this.y1, this.x2, this.y2) = (x1, y1, x2, y2);
        EasingMode = EasingMode.EaseIn;
    }
    public static readonly Bezier Spring = Frozen(0.25, 1.12, 0.5, 1), Out = Frozen(0.22, 1, 0.36, 1), Pop = Frozen(0.2, 0.8, 0.2, 1), Wave = Frozen(0.45, 0, 0.55, 1), Ripple = Frozen(0.25, 0.6, 0.3, 1);
    private static Bezier Frozen(double a, double b, double c, double d)
    {
        Bezier curve = new(a, b, c, d);
        curve.Freeze();
        return curve;
    }
    protected override double EaseInCore(double time)
    {
        // Solve x(s) = time for the curve parameter with Newton steps, then return y(s).
        double s = time;
        for (int i = 0; i < 8; i++)
        {
            double x = Sample(x1, x2, s) - time, slope = Slope(x1, x2, s);
            if (Math.Abs(x) < 1e-5 || Math.Abs(slope) < 1e-6)
                break;
            s = Math.Clamp(s - x / slope, 0, 1);
        }
        return Sample(y1, y2, s);
    }
    private static double Sample(double a, double b, double s) => 3 * a * (1 - s) * (1 - s) * s + 3 * b * (1 - s) * s * s + s * s * s;
    private static double Slope(double a, double b, double s) => 3 * a * (1 - s) * (1 - s) + 6 * (b - a) * (1 - s) * s + 3 * (1 - b) * s * s;
    protected override Freezable CreateInstanceCore() => new Bezier(x1, y1, x2, y2);
}

/// <summary>The design's motion: spring buttons, drifting glows, rising content, the menu and the living glass edges. Off in previews and when Windows animations are off.</summary>
internal static class Motion
{
    public static bool On => SystemParameters.ClientAreaAnimation && !(AppShell.Current?.Preview ?? true);
    private static bool registered;
    public static void Register()
    {
        if (registered)
            return;
        registered = true;
        EventManager.RegisterClassHandler(typeof(Button), UIElement.MouseEnterEvent, new MouseEventHandler((sender, _) => Hover((Button)sender, true)));
        EventManager.RegisterClassHandler(typeof(Button), UIElement.MouseLeaveEvent, new MouseEventHandler((sender, _) => Hover((Button)sender, false)));
        EventManager.RegisterClassHandler(typeof(Button), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((sender, _) => Press((Button)sender, true)), true);
        EventManager.RegisterClassHandler(typeof(Button), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((sender, _) => Press((Button)sender, false)), true);
    }
    private static bool Springs(Button button) => button.Style != button.TryFindResource("Plain") as Style && button.Focusable && button.IsEnabled;
    private static (ScaleTransform Scale, RotateTransform Rotate) Transforms(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform scale, RotateTransform rotate, ..] })
            return (scale, rotate);
        ScaleTransform s = new();
        RotateTransform r = new();
        element.RenderTransform = new TransformGroup { Children = { s, r, new TranslateTransform() } };
        element.RenderTransformOrigin = new(0.5, 0.5);
        return (s, r);
    }
    public static TranslateTransform Shift(UIElement element)
    {
        Transforms(element);
        return (TranslateTransform)((TransformGroup)element.RenderTransform).Children[2];
    }
    public static void To(Animatable target, DependencyProperty property, double value, double seconds, IEasingFunction? ease = null, double delay = 0) => target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(delay) });
    public static void To(UIElement target, DependencyProperty property, double value, double seconds, IEasingFunction? ease = null, double delay = 0) => target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(delay) });
    /// <summary>Hover: scale 1.02 with a 0.3° tilt that alternates between neighbours, and the icon wiggles to ∓6° then rests at ∓4°.</summary>
    private static void Hover(Button button, bool over)
    {
        if (!On || !Springs(button))
            return;
        (ScaleTransform scale, RotateTransform rotate) = Transforms(button);
        bool pick = button.Style == button.TryFindResource("Pick") as Style;
        int index = VisualTreeHelper.GetParent(button) is Panel panel ? panel.Children.IndexOf(button) : 0;
        double tilt = pick ? 0 : index % 2 == 0 ? -0.3 : 0.3;
        To(scale, ScaleTransform.ScaleXProperty, over ? 1.02 : 1, 0.45, Bezier.Spring);
        To(scale, ScaleTransform.ScaleYProperty, over ? 1.02 : 1, 0.45, Bezier.Spring);
        To(rotate, RotateTransform.AngleProperty, over ? tilt : 0, 0.45, Bezier.Spring);
        if (Glyph(button) is not FrameworkElement icon)
            return;
        (_, RotateTransform turn) = Transforms(icon);
        double side = index % 2 == 0 ? -1 : 1;
        if (over)
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.6), KeyFrames = { new EasingDoubleKeyFrame(6 * side, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)), Bezier.Spring), new EasingDoubleKeyFrame(4 * side, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6)), Bezier.Spring) } });
        else
            To(turn, RotateTransform.AngleProperty, 0, 0.3, Bezier.Spring);
    }
    private static void Press(Button button, bool down)
    {
        if (!On || !Springs(button))
            return;
        (ScaleTransform scale, _) = Transforms(button);
        double value = down ? 0.97 : button.IsMouseOver ? 1.02 : 1;
        To(scale, ScaleTransform.ScaleXProperty, value, down ? 0.15 : 0.45, Bezier.Spring);
        To(scale, ScaleTransform.ScaleYProperty, value, down ? 0.15 : 0.45, Bezier.Spring);
    }
    private static FrameworkElement? Glyph(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Viewbox box)
                return box;
            if (Glyph(child) is FrameworkElement found)
                return found;
        }
        return null;
    }
    /// <summary>Rise into place: up from <paramref name="distance"/> px while fading in (the design's hopo-rise and hopo-up).</summary>
    public static void Rise(UIElement element, double delay, double distance = 16, double seconds = 0.7)
    {
        if (!On)
            return;
        TranslateTransform shift = Shift(element);
        element.Opacity = 0;
        shift.Y = distance;
        To(element, UIElement.OpacityProperty, 1, seconds, Bezier.Out, delay);
        To(shift, TranslateTransform.YProperty, 0, seconds, Bezier.Out, delay);
    }
    /// <summary>Page entrance: each top-level block rises 16 px, 0.08 s apart.</summary>
    public static void Enter(FrameworkElement view)
    {
        if (view is Panel panel)
            for (int i = 0; i < panel.Children.Count && i < 8; i++)
                Rise(panel.Children[i], i * 0.08);
    }
    /// <summary>Glows drift and reverse forever: translate as a fraction of the glow's size, then scale.</summary>
    public static void Drift(FrameworkElement glow, double x, double y, double scale, double seconds)
    {
        if (!On)
            return;
        (ScaleTransform grow, _) = Transforms(glow);
        TranslateTransform move = Shift(glow);
        SineEase ease = new()
        {
            EasingMode = EasingMode.EaseInOut
        };
        foreach ((Animatable target, DependencyProperty property, double value) in new (Animatable, DependencyProperty, double)[] { (move, TranslateTransform.XProperty, x * glow.Width), (move, TranslateTransform.YProperty, y * glow.Height), (grow, ScaleTransform.ScaleXProperty, scale), (grow, ScaleTransform.ScaleYProperty, scale) })
            target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    /// <summary>Menu opens out of the menu button: scale .1 to 1 (visible by 40%), rows fade up 6 px from 0.12 s, 0.04 s apart.</summary>
    public static void OpenMenu(FrameworkElement scrim, FrameworkElement panel, IEnumerable<UIElement> rows)
    {
        if (!On)
            return;
        scrim.Opacity = 0;
        To(scrim, UIElement.OpacityProperty, 1, 0.4);
        (ScaleTransform scale, _) = Transforms(panel);
        panel.RenderTransformOrigin = new(1 - 36 / Math.Max(panel.ActualWidth, 380), 38 / Math.Max(panel.ActualHeight, 400));
        scale.ScaleX = scale.ScaleY = 0.1;
        To(scale, ScaleTransform.ScaleXProperty, 1, 0.58, Bezier.Pop);
        To(scale, ScaleTransform.ScaleYProperty, 1, 0.58, Bezier.Pop);
        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.58), KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)), new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.4)) } });
        int i = 0;
        foreach (UIElement row in rows)
            Rise(row, 0.12 + 0.04 * i++, 6, 0.5);
    }
    /// <summary>Menu row hover: shift 2 px right; the icon tile tilts ±4° and grows 4%.</summary>
    public static void Row(Button row, int index)
    {
        row.MouseEnter += (_, _) => RowHover(row, index, true);
        row.MouseLeave += (_, _) => RowHover(row, index, false);
    }
    private static void RowHover(Button row, int index, bool over)
    {
        // The row content is rebuilt when the menu opens, so look up its icon tile each time.
        if (!On || row.Content is not Panel { Children: [FrameworkElement tile, ..] })
            return;
        To(Shift(row), TranslateTransform.XProperty, over ? 2 : 0, 0.4, Bezier.Out);
        (ScaleTransform scale, RotateTransform rotate) = Transforms(tile);
        To(scale, ScaleTransform.ScaleXProperty, over ? 1.04 : 1, 0.45, Bezier.Spring);
        To(scale, ScaleTransform.ScaleYProperty, over ? 1.04 : 1, 0.45, Bezier.Spring);
        To(rotate, RotateTransform.AngleProperty, over ? (index % 2 == 0 ? -4 : 4) : 0, 0.45, Bezier.Spring);
    }
    /// <summary>Shared animated brushes for every glass card: a diagonal light sweeping across the edge every 9 s, and the purple-to-mint top line sliding ±22% over 7 s.</summary>
    public static void Glass(ResourceDictionary resources, bool dark, Color edge)
    {
        Color light = dark ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(90, 20, 22, 29);
        LinearGradientBrush sweep = new()
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops = { new(edge, 0), new(edge, 0.35), new(light, 0.48), new(edge, 0.6), new(edge, 1) },
            RelativeTransform = new TranslateTransform()
        };
        LinearGradientBrush sheen = new()
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 0),
            GradientStops = { new(Colors.Transparent, 0.18), new(Color.FromArgb(242, 200, 150, 255), 0.4), new(Color.FromArgb(204, 110, 255, 190), 0.62), new(Colors.Transparent, 0.82) },
            RelativeTransform = new TranslateTransform(),
            Opacity = 0.85
        };
        // The window's resources freeze a brush when it is added, so start the animations first (an animated brush cannot be frozen).
        if (On)
        {
            SineEase ease = new()
            {
                EasingMode = EasingMode.EaseInOut
            };
            ((TranslateTransform)sweep.RelativeTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.7, -0.7, TimeSpan.FromSeconds(9)) { EasingFunction = ease, RepeatBehavior = RepeatBehavior.Forever });
            ((TranslateTransform)sheen.RelativeTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-0.22, 0.22, TimeSpan.FromSeconds(7)) { EasingFunction = ease, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            sheen.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(7)) { EasingFunction = ease, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        resources["GlassEdge"] = sweep;
        resources["Sheen"] = sheen;
    }
}
