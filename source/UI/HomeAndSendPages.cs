using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using QRCoder;

namespace Lantoss.UI;

internal sealed class HomePage : IPage
{
    // The nearby field is drawn on the design's fixed 640 px stage and scaled down on small windows.
    private const double Stage = 640, Middle = Stage / 2;
    // The design's twelve wave rings: radius, wave height, lobes, colour, spin seconds, reversed, morph seconds, pointer pull.
    private static readonly (double R, double A, int N, string Color, double Spin, bool Reverse, double Morph, double Pull)[] Rings = [(268, 9, 14, "#B57CFF", 90, false, 7, 14), (273, 10, 18, "#3FE0FF", 130, true, 9, 8), (277, 8, 22, "#3DF5A0", 110, false, 6, 12), (282, 11, 12, "#FF6AD5", 160, true, 10, 6), (286, 7, 26, "#8C9BFF", 75, false, 6.5, 16), (290, 9, 16, "#D49BFF", 190, true, 9.5, 9), (280, 12, 10, "#7DF7C0", 145, false, 11, 11), (296, 6, 30, "#9AE6FF", 220, true, 8, 7), (271, 7, 24, "#C9A6FF", 100, true, 5.5, 13), (284, 10, 20, "#5CF0D0", 175, false, 8.5, 10), (292, 8, 13, "#FF9BE6", 120, false, 12, 5), (299, 5, 28, "#A7B6FF", 240, true, 7.5, 15)];
    private static readonly KeySpline Settle = Frozen(new KeySpline(0.22, 1, 0.36, 1));
    private readonly AppShell app;
    private readonly Canvas stage = new() { Width = Stage, Height = Stage }, devices = new() { Width = Stage, Height = Stage };
    private readonly System.Windows.Shapes.Path[] lines = new System.Windows.Shapes.Path[Rings.Length], glows = new System.Windows.Shapes.Path[Rings.Length];
    private readonly Border sheetHost = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 12) }, clickAway = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Button> dock = [];
    private readonly TextBlock status = C.Text("", 12, "Muted");
    private string? sheet, target, folder;
    private string draft = "";
    private bool upload = true;
    private double clock, rate = 1, proximity, last = -1;
    public FrameworkElement View
    {
        get;
    }
    public HomePage(AppShell app)
    {
        this.app = app;
        Canvas glowLayer = new()
        {
            Width = Stage,
            Height = Stage,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 6 }
        }, lineLayer = new()
        {
            Width = Stage,
            Height = Stage,
            IsHitTestVisible = false
        };
        for (int i = 0; i < Rings.Length; i++)
        {
            SolidColorBrush ink = new((Color)ColorConverter.ConvertFromString(Rings[i].Color));
            ink.Freeze();
            glows[i] = new()
            {
                Stroke = ink,
                StrokeThickness = 2.6,
                StrokeLineJoin = PenLineJoin.Round
            };
            lines[i] = new()
            {
                Stroke = ink,
                StrokeThickness = 0.8,
                StrokeLineJoin = PenLineJoin.Round
            };
            glowLayer.Children.Add(glows[i]);
            lineLayer.Children.Add(lines[i]);
        }
        stage.Children.Add(glowLayer);
        stage.Children.Add(lineLayer);
        foreach (double size in new[] { 470.0, 340 })
        {
            Ellipse ring = new()
            {
                Width = size,
                Height = size,
                StrokeThickness = 1,
                Opacity = 0.7
            };
            ring.SetResourceReference(Shape.StrokeProperty, "GlassBorder");
            Place(stage, ring, Middle - size / 2, Middle - size / 2);
        }
        // Water-glass ripples grow out of this PC every 12 s, 6 s apart.
        for (int i = 0; i < 2; i++)
        {
            ScaleTransform grow = new(0.6, 0.6);
            Ellipse ripple = new()
            {
                Width = 200,
                Height = 200,
                StrokeThickness = 1,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new(0.5, 0.5),
                RenderTransform = grow
            };
            ripple.SetResourceReference(Shape.FillProperty, "Glass");
            ripple.SetResourceReference(Shape.StrokeProperty, "Muted");
            Place(stage, ripple, Middle - 100, Middle - 100);
            if (!Motion.On)
            {
                (grow.ScaleX, grow.ScaleY, ripple.Opacity) = i == 0 ? (1.3, 1.3, 0.22) : (0.6, 0.6, 0);
                continue;
            }
            TimeSpan begin = TimeSpan.FromSeconds(-6 * i);
            DoubleAnimation spread = new(0.6, 2.1, TimeSpan.FromSeconds(12))
            {
                EasingFunction = Bezier.Ripple,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = begin
            };
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
            ripple.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(12), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)), new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.18)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) } });
        }
        Ellipse aura = new()
        {
            Width = 150,
            Height = 150,
            Effect = new DropShadowEffect { Color = Color.FromRgb(181, 124, 255), BlurRadius = 60, ShadowDepth = 0, Opacity = 0.35 }
        };
        aura.SetResourceReference(Shape.FillProperty, "Glass");
        Place(stage, aura, Middle - 75, Middle - 75);
        Border self = new()
        {
            Width = 150,
            Height = 150,
            CornerRadius = new(75),
            BorderThickness = new(1)
        };
        self.SetResourceReference(Border.BackgroundProperty, "Glass");
        self.SetResourceReference(Border.BorderBrushProperty, "GlassEdge");
        Ellipse beacon = new()
        {
            Width = 9,
            Height = 9,
            Margin = new(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Color.FromRgb(245, 181, 74), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 }
        };
        beacon.SetResourceReference(Shape.FillProperty, "Accent");
        TextBlock name = C.Text(app.Store.Preferences.DeviceName, 14, bold: true), label = C.Text("This PC", 11, "Muted");
        name.TextAlignment = label.TextAlignment = TextAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.MaxWidth = 128;
        StackPanel center = C.Stack(beacon, name, C.Space(label, 0, 2));
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        self.Child = center;
        Place(stage, self, Middle - 75, Middle - 75);
        stage.Children.Add(devices);
        Draw();
        // The dock: Send files, Folder, Text and Phone each raise their panel above it.
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal
        };
        foreach ((string key, string text, string icon) in new[] { ("send", "Send files", "up"), ("folder", "Folder", "folder"), ("text", "Text", "lines"), ("phone", "Phone", "phone") })
        {
            Button button = C.Button(text, () => Toggle(key), key == "send", icon);
            button.Margin = new(key == "send" ? 0 : 8, 0, 0, 0);
            dock[key] = button;
            buttons.Children.Add(button);
        }
        Border bar = C.Card(buttons, 8);
        bar.CornerRadius = new(18);
        bar.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 24, Opacity = 0.45, Direction = 270 };
        sheetHost.Effect = new DropShadowEffect { BlurRadius = 80, ShadowDepth = 30, Opacity = 0.5, Direction = 270 };
        status.HorizontalAlignment = HorizontalAlignment.Center;
        StackPanel bottom = new()
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(16, 0, 16, 18)
        };
        bottom.Children.Add(sheetHost);
        bottom.Children.Add(bar);
        bottom.Children.Add(C.Space(status, 0, 8));
        Grid field = new()
        {
            Background = Brushes.Transparent,
            Margin = new(0, 0, 0, 88)
        };
        field.Children.Add(new Viewbox { Child = stage, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        // Near the rings the field slows down and each ring swells or shrinks a little (the design's pointer proximity).
        field.MouseMove += (_, e) =>
        {
            Point point = e.GetPosition(stage);
            proximity = Math.Max(0, 1 - Math.Abs(Math.Sqrt((point.X - Middle) * (point.X - Middle) + (point.Y - Middle) * (point.Y - Middle)) - 284) / 110);
        };
        field.MouseLeave += (_, _) => proximity = 0;
        clickAway.MouseLeftButtonDown += (_, _) => Close();
        Grid layout = new()
        {
            AllowDrop = true,
            Background = Brushes.Transparent
        };
        layout.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportHeight)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1) });
        layout.Children.Add(field);
        layout.Children.Add(clickAway);
        layout.Children.Add(bottom);
        // Dropping files anywhere on Home adds them to the selection and opens the Send panel.
        AutomationProperties.SetName(layout, "Drop files or folders to prepare a transfer");
        layout.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        layout.Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { await app.AddSelectionsAsync(paths, false); Open("send", animate: sheet != "send"); } e.Handled = true; };
        layout.Loaded += (_, _) => { if (Motion.On) CompositionTarget.Rendering += Frame; };
        layout.Unloaded += (_, _) => CompositionTarget.Rendering -= Frame;
        Motion.Rise(bar, 0.15, 26, 0.9);
        View = layout;
    }
    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
    private static void Place(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }
    private void Frame(object? sender, EventArgs e)
    {
        double now = ((RenderingEventArgs)e).RenderingTime.TotalSeconds;
        if (now == last)
            return;
        if (last >= 0)
        {
            // Ease toward the design's playback rate, 1 − .75 × proximity, so the rings slow rather than stop.
            rate += (1 - 0.75 * proximity - rate) * 0.12;
            clock += Math.Min(now - last, 0.1) * rate;
        }
        last = now;
        Draw();
    }
    /// <summary>Each ring spins, morphs through the design's three shapes (A, 1.35 A, .5 A with shifted phase) and shimmers between .55 and 1.</summary>
    private void Draw()
    {
        for (int i = 0; i < Rings.Length; i++)
        {
            (double R, double A, int N, string _, double Spin, bool Reverse, double Morph, double Pull) = Rings[i];
            double p = i * 0.7, spin = (Reverse ? -2 : 2) * Math.PI * clock / Spin, u = clock / Morph % 1 * 3;
            int k = (int)u;
            double ease = Bezier.Wave.Ease(u - k);
            (double A, double Phase)[] shapes = [(A, p), (A * 1.35, p + Math.PI * 0.66), (A * 0.5, p + Math.PI * 1.33), (A, p)];
            double height = shapes[k].A + (shapes[k + 1].A - shapes[k].A) * ease, phase = shapes[k].Phase + (shapes[k + 1].Phase - shapes[k].Phase) * ease;
            double scale = 1 + proximity * Pull * (i % 3 == 1 ? -1 : 1) * 0.0045, half = Math.Floor(N / 2.0 + 0.5);
            StreamGeometry geometry = new();
            using (StreamGeometryContext path = geometry.Open())
                for (int step = 0; step <= 180; step++)
                {
                    double t = step * Math.PI / 90, r = (R + height * Math.Sin(N * t + phase) + height * 0.35 * Math.Sin(half * t - phase * 1.3)) * scale;
                    Point point = new(Middle + r * Math.Cos(t + spin), Middle + r * Math.Sin(t + spin));
                    if (step == 0)
                        path.BeginFigure(point, false, true);
                    else
                        path.LineTo(point, true, true);
                }
            geometry.Freeze();
            lines[i].Data = glows[i].Data = geometry;
            lines[i].Opacity = glows[i].Opacity = 0.55 + 0.45 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (clock + i * 0.6) / 7));
        }
    }
    public void Refresh()
    {
        devices.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => !p.Blocked && (p.Online || p.Paired) && p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.Online).ThenBy(p => p.Name).Take(8).ToArray();
        BrowserDevice[] browsers = app.Engine.Browser.Devices.Where(b => b.Approved && b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).Take(6).ToArray();
        int count = peers.Length + browsers.Length, index = 0;
        foreach (Peer peer in peers)
            Device(index++, count, peer.Name, peer.Online ? "Mint" : "Muted", peer.Name + (peer.Online ? ", on Wi‑Fi" : ", offline"), peer.Online ? "Online" : "Offline", peer.Online, "Wi‑Fi · " + peer.Address, peer.TrustLabel, peer.Paired ? C.Small("Send files", () => Open("send", peer.Fingerprint)) : C.Small("Connect", () => app.Dialogs.Pair(peer)), peer.Paired ? C.Small("Text", () => Open("text", peer.Fingerprint)) : null);
        foreach (BrowserDevice browser in browsers)
            Device(index++, count, browser.Label, "Phone", browser.Label + ", through the browser", "Browser", true, "Connected · " + browser.Address, "Approved on this PC", C.Small("Send files", () => Open("send", "web:" + browser.Handle)), C.Small("Disconnect", () => { if (MessageBox.Show(app.Window, "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect browser", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Browser.Disconnect(browser.Handle); }));
        if (count == 0)
        {
            TextBlock title = C.Text(app.Search.Length > 0 ? "No matching devices" : "Open hopo on your other PC", 15, bold: true), detail = C.Text("Use the same home Wi‑Fi. Your PCs and approved phones appear on these rings automatically.", 12, "Muted");
            title.TextAlignment = detail.TextAlignment = TextAlignment.Center;
            Button find = C.Button("Find a device", () => app.Dialogs.Pair(), icon: "devices");
            find.HorizontalAlignment = HorizontalAlignment.Center;
            Border card = C.Card(C.Stack(C.Space(title, 6), C.Space(detail, 14), find), 18);
            card.Width = 400;
            Place(devices, card, Middle - 200, Middle + 100);
        }
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        status.Text = speed > 0 ? $"{speed / 1048576:0.0} MB/s transferring" : "";
        status.Visibility = speed > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    /// <summary>A device: blinking dot with a radar ping and name tag. Hover or focus swaps the tag for its glass card.</summary>
    private void Device(int index, int count, string name, string color, string automation, string state, bool online, string address, string trust, Button first, Button? second)
    {
        // Alternate the inner and middle rings so neighbours never overlap their name tags.
        double radius = index % 2 == 0 ? 235 : 170, angle = (-140 + index * 360.0 / Math.Max(count, 1)) * Math.PI / 180;
        double x = Middle + radius * Math.Cos(angle), y = Middle + radius * Math.Sin(angle);
        bool left = x < Middle;
        Color tint = app.Window.TryFindResource(color) is SolidColorBrush brush ? brush.Color : Colors.White;
        ScaleTransform pulse = new(), echo = new(0.6, 0.6);
        Ellipse ping = new()
        {
            Width = 20,
            Height = 20,
            StrokeThickness = 1,
            Opacity = 0,
            RenderTransformOrigin = new(0.5, 0.5),
            RenderTransform = echo
        }, halo = new()
        {
            Width = 18,
            Height = 18,
            Opacity = 0.16
        }, core = new()
        {
            Width = 10,
            Height = 10,
            RenderTransformOrigin = new(0.5, 0.5),
            RenderTransform = pulse,
            Effect = new DropShadowEffect { Color = tint, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 }
        };
        ping.SetResourceReference(Shape.StrokeProperty, color);
        halo.SetResourceReference(Shape.FillProperty, color);
        core.SetResourceReference(Shape.FillProperty, color);
        Grid dot = new()
        {
            Width = 44,
            Height = 44
        };
        dot.Children.Add(ping);
        dot.Children.Add(halo);
        dot.Children.Add(core);
        if (Motion.On)
        {
            // Blink and ping on the design's 3.8 s beat, each device at its own point of the beat.
            TimeSpan begin = TimeSpan.FromSeconds(-1.2 * index % 3.8);
            SineEase smooth = new()
            {
                EasingMode = EasingMode.EaseInOut
            };
            core.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromSeconds(1.9)) { EasingFunction = smooth, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            DoubleAnimation beat = new(0.85, 1, TimeSpan.FromSeconds(1.9))
            {
                EasingFunction = smooth,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = begin
            };
            pulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            pulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
            DoubleAnimationUsingKeyFrames spread = new()
            {
                Duration = TimeSpan.FromSeconds(3.8),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = begin,
                KeyFrames = { new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(0)), new EasingDoubleKeyFrame(2.6, KeyTime.FromPercent(0.7), new QuadraticEase { EasingMode = EasingMode.EaseOut }), new EasingDoubleKeyFrame(2.6, KeyTime.FromPercent(1)) }
            };
            echo.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
            echo.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
            ping.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.8), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, KeyFrames = { new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0)), new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.7), new QuadraticEase { EasingMode = EasingMode.EaseOut }), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) } });
        }
        Button blip = new()
        {
            Style = (Style)app.Window.FindResource("Plain"),
            Width = 44,
            Height = 44,
            Content = dot
        };
        AutomationProperties.SetName(blip, automation);
        TextBlock caption = C.Text(name, 12);
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.MaxWidth = 150;
        Border tag = new()
        {
            CornerRadius = new(9),
            Padding = new(9, 5, 9, 5),
            BorderThickness = new(1),
            Child = caption,
            IsHitTestVisible = false
        };
        tag.SetResourceReference(Border.BackgroundProperty, "Menu");
        tag.SetResourceReference(Border.BorderBrushProperty, "GlassBorder");
        DockPanel top = new()
        {
            Margin = new(0, 0, 0, 8)
        };
        Border chip = C.Badge(state, online ? "Success" : "Muted", online ? (color == "Phone" ? "PhoneSoft" : "SuccessSoft") : "Glass");
        DockPanel.SetDock(chip, Dock.Right);
        top.Children.Add(chip);
        top.Children.Add(C.Text(name, 13, bold: true));
        TextBlock where = C.Text(address, 11, "Muted");
        where.FontFamily = new("Cascadia Mono, Consolas");
        first.Style = (Style)app.Window.FindResource("PrimaryButton");
        first.MinHeight = 34;
        first.Padding = new(12, 6, 12, 6);
        StackPanel actions = C.Inline(first);
        if (second != null)
        {
            second.Margin = new(6, 0, 0, 0);
            actions.Children.Add(second);
        }
        TranslateTransform rise = new(0, 8);
        ScaleTransform shrink = new(0.97, 0.97);
        Border card = new()
        {
            Width = 232,
            Padding = new(14),
            CornerRadius = new(16),
            BorderThickness = new(1),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { shrink, rise } },
            Child = C.Stack(top, where, C.Space(C.Text(trust, 11, "Muted"), 10, 3), actions)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassEdge");
        AutomationProperties.SetName(card, name + " details");
        Canvas group = new()
        {
            Width = Stage,
            Height = Stage
        };
        Place(group, blip, x - 22, y - 22);
        Canvas.SetTop(tag, y - 12);
        Canvas.SetTop(card, y > 430 ? y - 150 : y - 26);
        if (left)
        {
            Canvas.SetRight(tag, Stage - (x - 26));
            Canvas.SetRight(card, Stage - (x - 18));
        }
        else
        {
            Canvas.SetLeft(tag, x + 26);
            Canvas.SetLeft(card, x + 18);
        }
        group.Children.Add(tag);
        group.Children.Add(card);
        Panel.SetZIndex(group, 2);
        bool shown = false;
        void Show(bool on)
        {
            if (on == shown)
                return;
            shown = on;
            Panel.SetZIndex(group, on ? 6 : 2);
            card.IsHitTestVisible = on;
            double seconds = Motion.On ? 0.45 : 0;
            Motion.To(card, UIElement.OpacityProperty, on ? 1 : 0, Motion.On ? 0.3 : 0);
            Motion.To(tag, UIElement.OpacityProperty, on ? 0 : 1, Motion.On ? 0.25 : 0);
            Motion.To(rise, TranslateTransform.YProperty, on ? 0 : 8, seconds, Bezier.Out);
            Motion.To(shrink, ScaleTransform.ScaleXProperty, on ? 1 : 0.97, seconds, Bezier.Out);
            Motion.To(shrink, ScaleTransform.ScaleYProperty, on ? 1 : 0.97, seconds, Bezier.Out);
        }
        // A short grace period lets the pointer travel from the dot to its card.
        System.Windows.Threading.DispatcherTimer grace = new()
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        grace.Tick += (_, _) => { grace.Stop(); Show(group.IsMouseOver || group.IsKeyboardFocusWithin); };
        group.MouseEnter += (_, _) => { grace.Stop(); Show(true); };
        group.MouseLeave += (_, _) => grace.Start();
        group.IsKeyboardFocusWithinChanged += (_, _) => Show(group.IsKeyboardFocusWithin || group.IsMouseOver);
        blip.Click += (_, _) => Show(!shown);
        devices.Children.Add(group);
    }
    private void Toggle(string key)
    {
        if (sheet == key)
            Close();
        else
            Open(key);
    }
    private void Close()
    {
        sheet = null;
        sheetHost.Child = null;
        clickAway.Visibility = Visibility.Collapsed;
        MarkDock();
    }
    private void MarkDock()
    {
        foreach ((string key, Button button) in dock)
        {
            bool on = key == sheet;
            if (on)
            {
                button.SetResourceReference(Control.BackgroundProperty, key == "send" ? "AccentHover" : "GlassHover");
                button.SetResourceReference(Control.BorderBrushProperty, key == "send" ? "PrimarySoft" : "Muted");
                button.BorderThickness = new(key == "send" ? 3 : 1);
            }
            else
            {
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
                button.ClearValue(Control.BorderThicknessProperty);
            }
        }
    }
    /// <summary>Opens a panel above the dock. It grows out of its dock button: from 26 px lower at .62 × .5 with a 3 px blur, past full size at 55%, then rests (0.6 s); its rows fade up after 0.14 s.</summary>
    private void Open(string key, string? device = null, bool animate = true)
    {
        sheet = key;
        if (device != null)
            target = device;
        Border panel = key switch
        {
            "send" => SendSheet(),
            "folder" => FolderSheet(),
            "text" => TextSheet(),
            _ => PhoneSheet()
        };
        sheetHost.Child = panel;
        clickAway.Visibility = Visibility.Visible;
        MarkDock();
        if (!animate || !Motion.On)
            return;
        ScaleTransform scale = new(0.62, 0.5);
        TranslateTransform shift = new(0, 26);
        BlurEffect blur = new()
        {
            Radius = 3
        };
        panel.RenderTransformOrigin = new(key switch
        {
            "send" => 0.14,
            "folder" => 0.4,
            "text" => 0.61,
            _ => 0.84
        }, 1.08);
        panel.RenderTransform = new TransformGroup { Children = { scale, shift } };
        panel.Effect = blur;
        panel.Opacity = 0;
        DoubleAnimationUsingKeyFrames Keys(double from, double middle, double to) => new()
        {
            Duration = TimeSpan.FromSeconds(0.6),
            KeyFrames = { new DiscreteDoubleKeyFrame(from, KeyTime.FromPercent(0)), new SplineDoubleKeyFrame(middle, KeyTime.FromPercent(0.55), Settle), new SplineDoubleKeyFrame(to, KeyTime.FromPercent(1), Settle) }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(0.62, 1.006, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(0.5, 1.008, 1));
        shift.BeginAnimation(TranslateTransform.YProperty, Keys(26, -2, 0));
        panel.BeginAnimation(UIElement.OpacityProperty, Keys(0, 1, 1));
        DoubleAnimationUsingKeyFrames sharpen = Keys(3, 0, 0);
        sharpen.Completed += (_, _) => panel.Effect = null;
        blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
        if (panel.Child is Panel rows)
            foreach (UIElement row in rows.Children)
                Motion.Rise(row, 0.14, 6, 0.45);
    }
    private Border Sheet(string title, params UIElement[] parts)
    {
        Button close = new()
        {
            Width = 44,
            Height = 44,
            Padding = new(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Content = C.Icon("close", 16, "Muted")
        };
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Close();
        DockPanel head = new();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        TextBlock heading = C.Text(title, 15);
        heading.FontWeight = FontWeights.SemiBold;
        heading.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(heading);
        StackPanel body = C.Stack(head);
        foreach (UIElement part in parts)
            body.Children.Add(C.Space((FrameworkElement)part, 0, 14));
        Border panel = new()
        {
            Width = 440,
            Padding = new(18),
            CornerRadius = new(20),
            BorderThickness = new(1),
            Child = body
        };
        panel.SetResourceReference(Border.BackgroundProperty, "Surface");
        panel.SetResourceReference(Border.BorderBrushProperty, "GlassEdge");
        AutomationProperties.SetName(panel, title);
        return panel;
    }
    /// <summary>The dashed well used for "Drop files here" and "No folder chosen".</summary>
    private static Grid Well(string text, Button action)
    {
        Rectangle outline = new()
        {
            RadiusX = 14,
            RadiusY = 14,
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false,
            Opacity = 0.6
        };
        outline.SetResourceReference(Shape.StrokeProperty, "Muted");
        DockPanel row = new()
        {
            Margin = new(16)
        };
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        TextBlock label = C.Text(text, 13, "Muted");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = TextWrapping.NoWrap;
        row.Children.Add(label);
        Grid well = new();
        well.Children.Add(outline);
        well.Children.Add(row);
        return well;
    }
    /// <summary>"Send to" chips; the chosen one is highlighted in violet. A coloured dot marks PCs (mint) and phones (violet).</summary>
    private StackPanel Picks(string title, IEnumerable<(string Key, string Label, string? Dot)> items, Func<string, bool> chosen, Action<string> choose)
    {
        WrapPanel chips = new();
        foreach ((string key, string label, string? dot) in items)
        {
            StackPanel content = new()
            {
                Orientation = Orientation.Horizontal
            };
            if (dot != null)
            {
                Ellipse mark = new()
                {
                    Width = 7,
                    Height = 7,
                    Margin = new(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                mark.SetResourceReference(Shape.FillProperty, dot);
                content.Children.Add(mark);
            }
            content.Children.Add(C.Text(label, 13));
            Button chip = new()
            {
                Style = (Style)app.Window.FindResource("Pick"),
                Content = content,
                Tag = chosen(key) ? "sel" : null
            };
            AutomationProperties.SetName(chip, label);
            chip.Click += (_, _) => choose(key);
            chips.Children.Add(chip);
        }
        if (chips.Children.Count == 0)
            chips.Children.Add(C.Text("No paired device is available yet. Open Devices to pair one.", 12, "Muted"));
        return C.Stack(C.Space(C.Text(title, 12, "Muted"), 8), chips);
    }
    private Border SendSheet()
    {
        Peer[] peers = app.Engine.Peers.Where(p => p.Paired && !p.Blocked).OrderByDescending(p => p.Online).ThenBy(p => p.Name).ToArray();
        BrowserDevice[] phones = app.Engine.Browser.Devices.Where(b => b.Approved).ToArray();
        if (target == null || (!peers.Any(p => p.Fingerprint == target) && !phones.Any(b => "web:" + b.Handle == target)))
            target = app.SelectedPeerKey is string key && peers.Any(p => p.Fingerprint == key) ? key : peers.FirstOrDefault(p => p.Online)?.Fingerprint;
        string summary = app.Selection.Count == 0 ? "Drop files here" : $"{app.Selection.Count} selected · {app.Selection.Sum(item => item.Files)} files · {Format.Bytes(app.Selection.Sum(item => item.Size))}";
        Button choose = C.Button("Choose files", ChooseFiles, icon: "plus");
        Button send = C.Button("Send", Send, true, "send");
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        send.HorizontalContentAlignment = HorizontalAlignment.Center;
        send.IsEnabled = app.Selection.Count > 0 && target != null;
        return Sheet("Send files", Well(summary, choose), Picks("Send to", peers.Select(p => (p.Fingerprint, p.Name, (string?)(p.Online ? "Mint" : "Muted"))).Concat(phones.Select(b => ("web:" + b.Handle, b.Label, (string?)"Phone"))), key => key == target, key => { target = key; Open("send", animate: false); }), send);
    }
    private async void ChooseFiles()
    {
        OpenFileDialog picker = new()
        {
            Title = "Choose files to send",
            Multiselect = true,
            CheckFileExists = true
        };
        if (picker.ShowDialog(app.Window) != true)
            return;
        await app.AddSelectionsAsync(picker.FileNames, false);
        if (sheet == "send")
            Open("send", animate: false);
    }
    private async void Send()
    {
        string[] paths = app.Selection.Select(item => item.Path).ToArray();
        try
        {
            if (target?.StartsWith("web:", StringComparison.Ordinal) == true && app.Engine.Browser.Devices.FirstOrDefault(b => "web:" + b.Handle == target) is BrowserDevice phone)
            {
                Close();
                await Task.Run(() => app.Engine.Browser.ShareAsync(paths, default, phone.Handle));
                app.Tell("Sent to " + phone.Label + ". The files appear on that phone's page under Get files from your PC.");
            }
            else if (app.Engine.Peers.FirstOrDefault(p => p.Fingerprint == target && p.Paired && !p.Blocked) is Peer peer)
            {
                Close();
                _ = app.Engine.Send(peer, paths);
                app.Tell("Transfer queued for " + peer.Name + ". They approve before any file bytes are sent; follow it in Transfers.");
            }
        }
        catch (Exception error) { app.Error(error); }
    }
    private Border FolderSheet()
    {
        Button choose = C.Button("Choose folder", () =>
        {
            OpenFolderDialog picker = new()
            {
                Title = "Choose a folder to share"
            };
            if (picker.ShowDialog(app.Window) == true)
            {
                folder = picker.FolderName;
                Open("folder", animate: false);
            }
        }, icon: "folder");
        Button all = new()
        {
            Style = (Style)app.Window.FindResource("Plain"),
            Content = C.Text("All shared folders", 13, "Primary"),
            Padding = new(4, 8, 4, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        all.Click += (_, _) => { Close(); app.Navigate("Shared Spaces"); };
        Button start = C.Button("Start sharing", () =>
        {
            if (folder == null)
                return;
            SharedSpace space = app.Engine.Browser.Spaces.Add(folder);
            if (upload)
                app.Engine.Browser.Spaces.Update(space.Id, SpaceAccess.ReadAndUpload, true);
            app.Tell(space.Name + " is shared " + (upload ? "with Read & upload" : "read-only") + ". Paired PCs can open it in Shared folders.");
            folder = null;
            Close();
        }, true);
        start.IsEnabled = folder != null;
        DockPanel row = new();
        DockPanel.SetDock(start, Dock.Right);
        row.Children.Add(start);
        row.Children.Add(all);
        return Sheet("Share a folder", Well(folder ?? "No folder chosen", choose), Picks("Access", [("read", "Read only", null), ("up", "Read & upload", null)], key => key == (upload ? "up" : "read"), key => { upload = key == "up"; Open("folder", animate: false); }), row);
    }
    private Border TextSheet()
    {
        Peer[] peers = app.Engine.Peers.Where(p => p.Paired && !p.Blocked && p.Online).OrderBy(p => p.Name).ToArray();
        if (target == null || (target != "local" && !peers.Any(p => p.Fingerprint == target)))
            target = peers.FirstOrDefault()?.Fingerprint ?? "local";
        TextBox message = new()
        {
            Text = draft,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 88,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(message, "Message");
        message.TextChanged += (_, _) => draft = message.Text;
        Button share = C.AsyncButton("Share", async () =>
        {
            string content = SharedNotes.Validate(draft);
            if (target != "local" && app.Engine.Peers.FirstOrDefault(p => p.Fingerprint == target) is Peer peer)
                await app.Engine.SendNoteAsync(peer, content);
            else
                app.Engine.Browser.Notes.Add(content, "This PC", local: true);
            draft = "";
            Close();
            app.Tell("Text shared. Find it in Text & links on the receiving PC or its browser page.");
        }, true, "send");
        share.HorizontalAlignment = HorizontalAlignment.Stretch;
        share.HorizontalContentAlignment = HorizontalAlignment.Center;
        return Sheet("Share text or a link", C.Stack(C.Space(C.Text("Message", 12, "Muted"), 6), message), Picks("Send to", peers.Select(p => (p.Fingerprint, p.Name, (string?)"Mint")).Append(("local", "This PC (for my phone)", (string?)"Phone")), key => key == target, key => { target = key; Open("text", animate: false); }), share);
    }
    private Border PhoneSheet()
    {
        Border code = new()
        {
            Width = 112,
            Height = 112,
            CornerRadius = new(14),
            Margin = new(0, 0, 16, 0),
            Padding = new(6)
        };
        Button action;
        string address;
        if (app.Engine.Browser.Running)
        {
            using QRCodeGenerator generator = new();
            using QRCodeData data = generator.CreateQrCode(app.Engine.Browser.IpUrl, QRCodeGenerator.ECCLevel.Q);
            using PngByteQRCode qr = new(data);
            using MemoryStream image = new(qr.GetGraphic(6));
            code.Background = Brushes.White;
            code.Child = new Image { Source = BitmapFrame.Create(image, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad) };
            AutomationProperties.SetName(code, "QR code for " + app.Engine.Browser.IpUrl);
            address = app.Engine.Browser.IpUrl;
            action = C.Button("Open Phone & browser", () => { Close(); app.Navigate("Phone & browser"); }, true, "phone");
        }
        else
        {
            code.SetResourceReference(Border.BackgroundProperty, "Glass");
            code.SetResourceReference(Border.BorderBrushProperty, "GlassBorder");
            code.BorderThickness = new(1);
            code.Child = C.Icon("phone", 30, "Muted");
            address = "Browser sharing is off";
            action = C.Button("Start browser sharing", () =>
            {
                app.Store.SetPreferences(app.Store.Preferences with
                {
                    BrowserSharingEnabled = true
                });
                if (!app.Engine.Running)
                    app.ToggleReceiving();
                else
                    app.Engine.ApplySavedPreferences();
                Open("phone", animate: false);
            }, true, "phone");
        }
        action.HorizontalAlignment = HorizontalAlignment.Stretch;
        action.HorizontalContentAlignment = HorizontalAlignment.Center;
        TextBlock link = C.Text(address, 12, "Muted");
        link.FontFamily = new("Cascadia Mono, Consolas");
        StackPanel words = C.Stack(C.Space(C.Text("Same Wi‑Fi, then scan with your camera.", 13, "Muted"), 8), link);
        words.VerticalAlignment = VerticalAlignment.Center;
        DockPanel row = new();
        DockPanel.SetDock(code, Dock.Left);
        row.Children.Add(code);
        row.Children.Add(words);
        return Sheet("Open on your phone", row, action);
    }
}

internal sealed class SendPage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel files = new(), recipient = new();
    private readonly TextBlock totals = C.Text("", 12, "Muted");
    public FrameworkElement View
    {
        get;
    }
    public SendPage(AppShell app)
    {
        this.app = app;
        Button addFiles = C.Button("Add files", app.PickFiles, icon: "plus"), addFolders = C.Button("Add folders", app.PickFolders, icon: "folder");
        addFolders.Margin = new(9, 0, 0, 0);
        StackPanel left = C.Stack(C.Space(C.Heading("Selected files & folders", C.Small("Clear selection", () => { app.Selection.Clear(); Refresh(); })), 6), C.Space(C.Inline(addFiles, addFolders), 18), C.Space(totals, 16), files);
        Button phone = C.Button("Share selection with phone/browser", async () => { if (app.Selection.Count == 0) { app.Navigate("Phone & browser"); return; } try { await Task.Run(() => app.Engine.Browser.ShareAsync(app.Selection.Select(item => item.Path).ToArray())); app.Navigate("Phone & browser"); } catch (Exception error) { app.Error(error); } }, icon: "devices");
        StackPanel right = C.Stack(C.Card(recipient), C.Space(phone, 0, 16), C.Space(C.Note("The recipient stays in control", "They see your identity, file count, size, and destination, then choose Accept or Decline."), 0, 18), C.Space(C.Note("Native transfers: original bytes, verified", "Folders keep their relative paths. Every native file is checked before it is finalized on the other PC.", "check"), 0, 18), C.Space(C.Text("Names, folder paths, and regular file bytes are preserved. Permissions, timestamps, alternate data streams, links, and other platform metadata are not copied.", 11, "Muted"), 0, 18));
        View = C.Stack(C.Hero("Share", "Send files", "Pick files, then a device. They approve before anything moves."), C.Columns(C.Card(left), right, 325));
    }
    public void Refresh()
    {
        files.Children.Clear();
        totals.Text = $"{app.Selection.Count} selections · {app.Selection.Sum(s => s.Files)} files · {Format.Bytes(app.Selection.Sum(s => s.Size))}";
        SelectionItem[] visible = app.Selection.Where(s => s.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visible.Length == 0)
            files.Children.Add(C.Space(C.Text(app.Selection.Count == 0 ? "Add files or folders to start a transfer." : "No selection matches your search.", 13, "Muted"), 30, 20));
        foreach (SelectionItem item in visible)
        {
            FrameworkElement icon = C.Icon(item.Folders > 0 ? "folder" : "file", 27);
            icon.Margin = new(0, 0, 14, 0);
            Button remove = C.Small("Remove", () => { app.Selection.Remove(item); Refresh(); });
            remove.VerticalAlignment = VerticalAlignment.Center;
            DockPanel row = new()
            {
                Margin = new(0, 0, 0, 14)
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            StackPanel info = C.Stack(C.Space(C.Text(item.Name, 13, bold: true), 5), C.Text(item.Detail, 11, "Muted"));
            info.Margin = new(0, 4, 10, 4);
            row.Children.Add(info);
            files.Children.Add(row);
        }
        recipient.Children.Clear();
        recipient.Children.Add(C.Heading("Send to"));
        // Approved phones and browsers are recipients too: the files appear only on that phone's page.
        BrowserDevice[] phones = app.Engine.Browser.Running ? app.Engine.Browser.Devices.Where(device => device.Approved).ToArray() : [];
        if (phones.Length > 0)
            recipient.Children.Add(C.Space(C.Text("Phones and browsers", 12, bold: true), 10));
        foreach (BrowserDevice device in phones)
        {
            Button toPhone = C.Small("Send", () => SendToPhone(device));
            toPhone.IsEnabled = app.Selection.Count > 0;
            toPhone.VerticalAlignment = VerticalAlignment.Center;
            DockPanel line = new()
            {
                Margin = new(0, 0, 0, 10)
            };
            DockPanel.SetDock(toPhone, Dock.Right);
            line.Children.Add(toPhone);
            line.Children.Add(C.Person(device.Label, device.Picture, "Phone / browser · " + device.Address, 34, "Success"));
            recipient.Children.Add(line);
        }
        if (phones.Length > 0)
            recipient.Children.Add(C.Space(C.Text("Computers", 12, bold: true), 8, 10));
        Peer[] paired = app.Engine.Peers.Where(p => p.Paired && !p.Blocked).ToArray();
        if (paired.Length == 0)
        {
            recipient.Children.Add(C.Space(C.Text("Connect another PC once, then it will be remembered for future transfers.", 12, "Muted"), 18));
            recipient.Children.Add(C.Button("Connect a PC", () => app.Dialogs.Pair(), true, "pair"));
            return;
        }
        ComboBox picker = new()
        {
            ItemsSource = paired,
            DisplayMemberPath = "Name",
            Margin = new(0, 0, 0, 14)
        };
        picker.SelectedItem = paired.FirstOrDefault(p => p.Fingerprint == app.SelectedPeerKey) ?? paired[0];
        AutomationProperties.SetName(picker, "Paired recipient");
        picker.SelectionChanged += (_, _) => app.SelectedPeerKey = (picker.SelectedItem as Peer)?.Fingerprint;
        recipient.Children.Add(picker);
        recipient.Children.Add(C.Space(C.Text("The public key will be checked again when connecting. Offline devices may need receiving switched on.", 11, "Muted"), 20));
        Button send = C.Button("Send securely", () => { if (picker.SelectedItem is Peer peer) app.SendSelected(peer); }, true, "send");
        send.IsEnabled = app.Selection.Count > 0;
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        recipient.Children.Add(send);
        recipient.Children.Add(C.Space(C.Text("Duplicate handling is chosen by the recipient. Existing files are never silently overwritten.", 11, "Muted"), 0, 18));
    }
    private async void SendToPhone(BrowserDevice device)
    {
        try
        {
            string[] paths = app.Selection.Select(item => item.Path).ToArray();
            await Task.Run(() => app.Engine.Browser.ShareAsync(paths, default, device.Handle));
            app.Tell("Sent to " + device.Label + ". The files appear on that phone's page under Get files from your PC.");
        }
        catch (Exception error) { app.Error(error); }
    }
}
