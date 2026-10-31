using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace Lantoss.Desktop;

/// <summary>
/// The design's motion: spring buttons with a wiggling icon, living glass cards, drifting glows and staggered entrances.
/// It is off in previews and renders, and when macOS or GNOME asks for reduced motion; every element then rests in its settled state.
/// </summary>
internal static class Motion
{
    public static bool Enabled { get; private set; } = true;
    public static readonly SplineEasing Spring = new(.25, 1.12, .5, 1), Out = new(.22, 1, .36, 1), Grow = new(.2, .8, .2, 1), InOut = new(.42, 0, .58, 1), EaseOut = new(0, 0, .58, 1), Settle = new(.25, 1, .5, 1), Tilt = new(.25, 1.1, .5, 1), Morph = new(.45, 0, .55, 1), Ripple = new(.25, .6, .3, 1);
    private static bool installed;
    /// <summary>Keyframes on RenderTransform interpolate between transform lists ("translateY(26px) scale(.62, .5)").</summary>
    private sealed class TransformsAnimator : InterpolatingAnimator<ITransform>
    {
        public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue) => TransformOperations.Interpolate(oldValue as TransformOperations ?? TransformOperations.Identity, newValue as TransformOperations ?? TransformOperations.Identity, progress);
    }
    static Motion() => Animation.RegisterCustomAnimator<ITransform, TransformsAnimator>();
    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);
    public static ITransform Transform(string value) => TransformOperations.Parse(value);
    public static double Cycle(double value) => value - Math.Floor(value);
    /// <summary>0 → 1 → 0 over one cycle: an alternating animation's progress.</summary>
    public static double Triangle(double value) => value < .5 ? value * 2 : 2 - value * 2;
    private static RelativePoint At(double x, double y) => new(x, y, RelativeUnit.Relative);
    private static void Run(Animation animation, Animatable target) => _ = animation.RunAsync(target);
    private static KeyFrame Frame(double cue, params (AvaloniaProperty Property, object? Value)[] values)
    {
        KeyFrame frame = new()
        {
            Cue = new Cue(cue)
        };
        foreach ((AvaloniaProperty property, object? value) in values)
            frame.Setters.Add(new Setter(property, value));
        return frame;
    }
    private static Transitions Moves(double seconds, Easing easing) => new() { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Seconds(seconds), Easing = easing } };
    private static Setter Transformed(string value) => new(Visual.RenderTransformProperty, Transform(value));
    /// <summary>Decides once whether the app moves, then adds the button, chip and menu-row styles.</summary>
    public static void Setup(bool preview)
    {
        Enabled = !preview && !SystemReduced();
        if (installed)
            return;
        installed = true;
        Styles styles = Application.Current!.Styles;
        // The open dock button keeps an amber ring; this is state, not motion, so it is always on.
        styles.Add(new Style(x => x.OfType<Button>().Class("on").Class("primary").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")) { Setters = { new Setter(ContentPresenter.BoxShadowProperty, new BoxShadows(new BoxShadow { Spread = 3, Color = Color.Parse("#47F5B54A") })) } });
        if (!Enabled)
            return;
        static Selector Springy(Selector? x) => x.OfType<Button>().Not(y => y.Class("row")).Not(y => y.Class("plain"));
        static Animation Wiggle(double peak, double rest) => new()
        {
            Duration = Seconds(.6),
            Easing = Settle,
            FillMode = FillMode.Forward,
            Children = { Frame(0, (Visual.RenderTransformProperty, Transform("rotate(0deg)"))), Frame(.5, (Visual.RenderTransformProperty, Transform($"rotate({peak}deg)"))), Frame(1, (Visual.RenderTransformProperty, Transform($"rotate({rest}deg)"))) }
        };
        styles.Add(new Style(x => x.OfType<Button>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")) { Setters = { new Setter(Animatable.TransitionsProperty, new Transitions { new BrushTransition { Property = ContentPresenter.BackgroundProperty, Duration = Seconds(.25) }, new BrushTransition { Property = ContentPresenter.BorderBrushProperty, Duration = Seconds(.25) } }) } });
        // Hover: a 2% swell with a 0.3° tilt that alternates between neighbours, on a spring with a slight overshoot. Press: 97% in 0.15 s.
        styles.Add(new Style(x => Springy(x)) { Setters = { new Setter(Animatable.TransitionsProperty, Moves(.45, Spring)) } });
        styles.Add(new Style(x => Springy(x).Class(":pointerover")) { Setters = { Transformed("scale(1.02) rotate(-0.3deg)") } });
        styles.Add(new Style(x => Springy(x).NthChild(2, 0).Class(":pointerover")) { Setters = { Transformed("scale(1.02) rotate(0.3deg)") } });
        styles.Add(new Style(x => Springy(x).Class(":pressed")) { Setters = { new Setter(Animatable.TransitionsProperty, Moves(.15, Spring)), Transformed("scale(0.97)") } });
        styles.Add(new Style(x => Springy(x).Not(y => y.NthChild(2, 0)).Class(":pointerover").Descendant().Class("icon")) { Animations = { Wiggle(-6, -4) } });
        styles.Add(new Style(x => Springy(x).NthChild(2, 0).Class(":pointerover").Descendant().Class("icon")) { Animations = { Wiggle(6, 4) } });
        // Menu rows shift right and tilt their icon tile instead.
        styles.Add(new Style(x => x.OfType<Button>().Class("row")) { Setters = { new Setter(Animatable.TransitionsProperty, Moves(.4, Settle)) } });
        styles.Add(new Style(x => x.OfType<Button>().Class("row").Class(":pointerover")) { Setters = { Transformed("translateX(2px)") } });
        styles.Add(new Style(x => x.OfType<Border>().Class("tile")) { Setters = { new Setter(Animatable.TransitionsProperty, Moves(.45, Tilt)) } });
        styles.Add(new Style(x => x.OfType<Button>().Class("row").Class(":pointerover").Descendant().Class("tile")) { Setters = { Transformed("rotate(-4deg) scale(1.04)") } });
        styles.Add(new Style(x => x.OfType<Button>().Class("row").NthChild(2, 1).Class(":pointerover").Descendant().Class("tile")) { Setters = { Transformed("rotate(4deg) scale(1.04)") } });
    }
    /// <summary>macOS "Reduce motion" and GNOME "Animations" off. Other desktops expose no common setting, so motion stays on there.</summary>
    private static bool SystemReduced()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return false;
        try
        {
            ProcessStartInfo info = OperatingSystem.IsMacOS() ? new("defaults") : new("gsettings");
            foreach (string argument in OperatingSystem.IsMacOS() ? new[] { "read", "com.apple.universalaccess", "reduceMotion" } : new[] { "get", "org.gnome.desktop.interface", "enable-animations" })
                info.ArgumentList.Add(argument);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = info.RedirectStandardError = true;
            using Process? process = Process.Start(info);
            if (process == null || !process.WaitForExit(1500))
                return false;
            string value = process.StandardOutput.ReadToEnd().Trim();
            return OperatingSystem.IsMacOS() ? value == "1" : value == "false";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException) { return false; }
    }
    /// <summary>
    /// Calls <paramref name="frame"/> once per frame with the seconds since <paramref name="host"/> appeared, only while it is on screen,
    /// so pages that are gone stop ticking. Endless effects use this: Avalonia animations cannot run forever on brushes.
    /// </summary>
    public static void Every(Visual host, Action<double> frame)
    {
        if (!Enabled)
            return;
        int run = 0;
        host.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(host) is not { } top)
                return;
            int current = ++run;
            TimeSpan? first = null;
            void Tick(TimeSpan now)
            {
                if (current != run)
                    return;
                first ??= now;
                frame((now - first.Value).TotalSeconds);
                top.RequestAnimationFrame(Tick);
            }
            top.RequestAnimationFrame(Tick);
        };
        host.DetachedFromVisualTree += (_, _) => run++;
    }
    /// <summary>The card's edge sweep, every 9 s.</summary>
    public static void Card(Visual card, LinearGradientBrush sweep) => Every(card, time =>
    {
        double pass = InOut.Ease(Cycle(time / 9)) * 2.8;
        sweep.StartPoint = At(-2.4 + pass, 0);
        sweep.EndPoint = At(.6 + pass, 1.4);
    });
    /// <summary>One background glow drifting by a share of the window and scaling, easing in and out and reversing forever.</summary>
    public static void Drift(Visual field, RadialGradientBrush glow, double x, double y, double scale, double seconds)
    {
        Point from = glow.Center.Point;
        double radiusX = glow.RadiusX.Scalar, radiusY = glow.RadiusY.Scalar;
        Every(field, time =>
        {
            double progress = InOut.Ease(Triangle(Cycle(time / (2 * seconds)))), size = 1 + (scale - 1) * progress;
            glow.Center = glow.GradientOrigin = At(from.X + x * progress, from.Y + y * progress);
            glow.RadiusX = new RelativeScalar(radiusX * size, RelativeUnit.Relative);
            glow.RadiusY = new RelativeScalar(radiusY * size, RelativeUnit.Relative);
        });
    }
    /// <summary>Rises <paramref name="distance"/> px and fades in; hidden until its delay ends.</summary>
    public static void Rise(Visual target, double distance, double seconds, double delay = 0, Easing? easing = null)
    {
        if (!Enabled)
            return;
        Animation animation = new()
        {
            Duration = Seconds(seconds),
            Delay = Seconds(delay),
            Easing = easing ?? Out,
            FillMode = FillMode.Backward,
            Children = { Frame(0, (Visual.OpacityProperty, 0d), (Visual.RenderTransformProperty, Transform($"translateY({distance}px)"))), Frame(1, (Visual.OpacityProperty, 1d), (Visual.RenderTransformProperty, Transform("translateY(0px)"))) }
        };
        Run(animation, target);
    }
    /// <summary>Page entrance: each top-level block rises 16 px over 0.7 s, 0.08 s apart.</summary>
    public static void Enter(Control view)
    {
        if (view is Panel panel)
            for (int index = 0; index < panel.Children.Count; index++)
                Rise(panel.Children[index], 16, .7, index * .08);
        else
            Rise(view, 16, .7);
    }
    public static void Fade(Visual target, double seconds)
    {
        if (Enabled)
            Run(new Animation { Duration = Seconds(seconds), Easing = EaseOut, Children = { Frame(0, (Visual.OpacityProperty, 0d)), Frame(1, (Visual.OpacityProperty, 1d)) } }, target);
    }
    /// <summary>The menu grows from its button: scale .1 to 1 over 0.58 s, fully visible by 40%.</summary>
    public static void Open(Visual panel)
    {
        if (Enabled)
            Run(new Animation { Duration = Seconds(.58), Easing = Grow, Children = { Frame(0, (Visual.OpacityProperty, 0d), (Visual.RenderTransformProperty, Transform("scale(0.1)"))), Frame(.4, (Visual.OpacityProperty, 1d)), Frame(1, (Visual.OpacityProperty, 1d), (Visual.RenderTransformProperty, Transform("scale(1)"))) } }, panel);
    }
    /// <summary>A Home panel grows out of its dock button: squashed, lowered and blurred, a little past full size at 55%, then at rest.</summary>
    public static void Pop(Visual sheet)
    {
        if (Enabled)
            Run(new Animation { Duration = Seconds(.6), Easing = Out, Children = { Frame(0, (Visual.OpacityProperty, 0d), (Visual.RenderTransformProperty, Transform("translateY(26px) scale(0.62, 0.5)")), (Visual.EffectProperty, new BlurEffect { Radius = 3 })), Frame(.55, (Visual.OpacityProperty, 1d), (Visual.RenderTransformProperty, Transform("translateY(-2px) scale(1.006, 1.008)")), (Visual.EffectProperty, new BlurEffect { Radius = 0 })), Frame(1, (Visual.OpacityProperty, 1d), (Visual.RenderTransformProperty, Transform("translateY(0px) scale(1, 1)")), (Visual.EffectProperty, new BlurEffect { Radius = 0 })) } }, sheet);
    }
    /// <summary>Opacity and transform changes ease in place of jumping (device cards and name tags).</summary>
    public static void Ease(Animatable target, double opacitySeconds, double moveSeconds = 0)
    {
        if (!Enabled)
            return;
        Transitions transitions = new() { new DoubleTransition { Property = Visual.OpacityProperty, Duration = Seconds(opacitySeconds), Easing = InOut } };
        if (moveSeconds > 0)
            transitions.Add(new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Seconds(moveSeconds), Easing = Out });
        target.Transitions = transitions;
    }
}
