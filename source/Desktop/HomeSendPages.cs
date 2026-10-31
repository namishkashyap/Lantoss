using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lantoss.Desktop;

/// <summary>
/// The nearby field: this computer in the middle, paired PCs (mint) and phones or browsers (violet) blinking on the rings, twelve glowing wave
/// rings and two water ripples around them, and a dock whose buttons raise the Send files, Folder, Text and Phone panels.
/// </summary>
internal sealed class HomePage : IPage
{
    private const double Size = 640, Middle = 320, CardWidth = 232;
    private static readonly double[] Orbits = [170, 235];
    // The design's twelve wave rings: radius, wave height, lobes, colour, spin period in seconds (negative turns the other way), morph period, and how much the pointer swells or shrinks it.
    private static readonly (double Radius, double Height, int Lobes, string Color, double Spin, double Morph, double Pull)[] Waves = [(268, 9, 14, "#B57CFF", 90, 7, 14), (273, 10, 18, "#3FE0FF", -130, 9, 8), (277, 8, 22, "#3DF5A0", 110, 6, 12), (282, 11, 12, "#FF6AD5", -160, 10, 6), (286, 7, 26, "#8C9BFF", 75, 6.5, 16), (290, 9, 16, "#D49BFF", -190, 9.5, 9),
        (280, 12, 10, "#7DF7C0", 145, 11, 11), (296, 6, 30, "#9AE6FF", -220, 8, 7), (271, 7, 24, "#C9A6FF", -100, 5.5, 13), (284, 10, 20, "#5CF0D0", 175, 8.5, 10), (292, 8, 13, "#FF9BE6", 120, 12, 5), (299, 5, 28, "#A7B6FF", -240, 7.5, 15)];
    // Each ring morphs through three shapes (A, 1.35 A and 0.5 A with shifted phase), kept as radii sampled every 2°.
    private static readonly double[][][] Shapes = [.. Waves.Select((wave, index) => new[] { Wave(wave.Radius, wave.Height, wave.Lobes, index * .7), Wave(wave.Radius, wave.Height * 1.35, wave.Lobes, index * .7 + Math.PI * .66), Wave(wave.Radius, wave.Height * .5, wave.Lobes, index * .7 + Math.PI * 1.33) })];
    private sealed record Spot(string Key, string Name, string Color, Peer? Peer, BrowserDevice? Browser);
    private sealed record Target(string Label, string Color, Peer? Peer, BrowserDevice? Browser);
    private sealed record Blink(Border Dot, ScaleTransform Beat, Ellipse Ping, ScaleTransform Spread, double Offset);
    private readonly Shell app;
    private readonly Canvas stage = new() { Width = Size, Height = Size, Background = Brushes.Transparent }, devices = new() { Width = Size, Height = Size };
    private readonly ContentControl activity = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Path[] lines = new Path[Waves.Length], glows = new Path[Waves.Length];
    private readonly (Border Ripple, ScaleTransform Scale)[] ripples = new (Border, ScaleTransform)[2];
    private readonly List<Blink> blinks = [];
    private readonly List<Action> cards = [];
    private readonly Dictionary<string, Button> dock = [];
    private readonly Panel sheetHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(16, 0, 16, 12) };
    private readonly Border clickAway = new() { Background = Brushes.Transparent, IsVisible = false };
    private readonly TextBox message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 84, MaxHeight = 160, MaxLength = SharedNotes.MaxCharacters, PlaceholderText = "Paste a note or a link…", CornerRadius = new(12) };
    private string? open, sheet, folder, noteKey, deviceSignature;
    private SpaceAccess access = SpaceAccess.ReadOnly;
    private Action? fillSheet;
    private double clock, pull, nearness, last;
    public Control View
    {
        get;
    }
    public HomePage(Shell app)
    {
        this.app = app;
        // Wave rings sit outside the two inner rings; a blurred copy of every ring under its 0.55 px stroke is the glow.
        Canvas glowLayer = new()
        {
            Width = Size,
            Height = Size,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 6 }
        }, lineLayer = new()
        {
            Width = Size,
            Height = Size,
            IsHitTestVisible = false
        };
        for (int index = 0; index < Waves.Length; index++)
        {
            IBrush color = Theme.Brush(Waves[index].Color);
            glowLayer.Children.Add(glows[index] = new Path { Stroke = color, StrokeThickness = 2, StrokeJoin = PenLineJoin.Round });
            lineLayer.Children.Add(lines[index] = new Path { Stroke = color, StrokeThickness = .55, StrokeJoin = PenLineJoin.Round });
        }
        stage.Children.Add(glowLayer);
        stage.Children.Add(lineLayer);
        stage.Children.Add(Ring(235, "Text/9"));
        stage.Children.Add(Ring(170, "Text/11"));
        // Water ripples: two glass circles grow from the centre, 6 s apart.
        for (int index = 0; index < ripples.Length; index++)
        {
            ScaleTransform scale = new();
            Border ripple = new()
            {
                Width = 200,
                Height = 200,
                CornerRadius = new(100),
                BorderThickness = new(1),
                BorderBrush = Theme.Brush(Theme.Dark ? "#FFFFFF/22" : "Text/14"),
                Background = new RadialGradientBrush { Center = new RelativePoint(.5, .28, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.5, .28, RelativeUnit.Relative), RadiusX = new RelativeScalar(.875, RelativeUnit.Relative), RadiusY = new RelativeScalar(.875, RelativeUnit.Relative), GradientStops = [new(Theme.Color("#FFFFFF/9"), 0), new(Theme.Color("#FFFFFF/1.5"), .62), new(Theme.Color("#B57CFF/5"), 1)] },
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 26, Color = Theme.Color("#B57CFF/12") }),
                RenderTransform = scale,
                IsHitTestVisible = false
            };
            ripples[index] = (ripple, scale);
            stage.Children.Add(Place(ripple, Middle - 100, Middle - 100));
        }
        Border glow = new()
        {
            Width = 9,
            Height = 9,
            CornerRadius = new(4.5),
            Background = Theme.Brush("Accent"),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Theme.Color("Accent/80") }),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(0, 0, 0, 6)
        };
        TextBlock name = C.Text(app.Store.Preferences.DeviceName, 14, bold: true), kind = C.Text("This " + Shell.Platform, 11, "Muted");
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 122;
        name.HorizontalAlignment = kind.HorizontalAlignment = HorizontalAlignment.Center;
        StackPanel label = C.Stack(glow, name, C.Space(kind, 0, 2));
        label.VerticalAlignment = VerticalAlignment.Center;
        stage.Children.Add(Place(new Border { Width = 150, Height = 150, CornerRadius = new(75), Background = Theme.Brush("Glass"), BorderBrush = Theme.Brush("Text/18"), BorderThickness = new(1), BoxShadow = new BoxShadows(new BoxShadow { Blur = 60, Color = Theme.Color(Theme.Dark ? "GlowA/35" : "GlowA/25") }), Child = label, IsHitTestVisible = false }, Middle - 75, Middle - 75));
        stage.Children.Add(devices);
        foreach ((string key, string text, string icon) in new[] { ("send", "Send files", "send"), ("folder", "Folder", "folder"), ("text", "Text", "text"), ("phone", "Phone", "phone") })
            dock[key] = C.Button(text, () => Open(sheet == key ? null : key), key == "send", icon);
        Border bar = new()
        {
            Child = C.Inline([.. dock.Values]),
            Padding = new(8),
            CornerRadius = new(18),
            Background = Theme.Brush(Theme.Dark ? "#0C0C14/50" : "Glass"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            BoxShadow = Theme.Shadow(24, 60, 0.45),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        StackPanel foot = C.Stack(activity, bar);
        foot.Spacing = 12;
        foot.Margin = new(16, 0, 16, 24);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        Grid layout = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = Brushes.Transparent
        };
        Grid.SetRow(foot, 1);
        Grid.SetRowSpan(clickAway, 2);
        clickAway.PointerPressed += (_, _) => Open(null);
        // Faint outer rings continue the field to the window edges behind the stage; the stage itself may overflow its box when the rings swell.
        layout.Children.Add(new Border { Child = C.Rings(360, 820, "Ring", 364), Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        layout.Children.Add(new Viewbox { Child = stage, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(16, 8, 16, 12), ClipToBounds = false });
        layout.Children.Add(clickAway);
        layout.Children.Add(sheetHost);
        layout.Children.Add(foot);
        // Clicking empty space unpins a device card.
        layout.PointerPressed += (_, e) =>
        {
            if (open != null && (ReferenceEquals(e.Source, stage) || ReferenceEquals(e.Source, layout)))
                Pin(null);
        };
        // The pointer near the wave rings slows the field and swells or shrinks each ring: 1 at radius 284, 0 at ±110 px.
        layout.PointerMoved += (_, e) =>
        {
            Point point = e.GetPosition(stage);
            nearness = Math.Max(0, 1 - Math.Abs(Math.Sqrt((point.X - Middle) * (point.X - Middle) + (point.Y - Middle) * (point.Y - Middle)) - 284) / 110);
        };
        layout.PointerExited += (_, _) => nearness = 0;
        layout.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && sheet != null)
            {
                Open(null);
                e.Handled = true;
            }
        };
        layout.AttachedToVisualTree += (_, _) => last = 0;
        Motion.Every(layout, Step);
        Motion.Rise(bar, 26, .9, .15);
        View = layout;
        Draw();
    }
    private static T Place<T>(T control, double x, double y) where T : Control
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        return control;
    }
    private static Ellipse Ring(double radius, string stroke) => Place(new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Theme.Brush(stroke), StrokeThickness = 1, IsHitTestVisible = false }, Middle - radius, Middle - radius);
    private static double[] Wave(double radius, double height, int lobes, double phase) => [.. Enumerable.Range(0, 181).Select(step => step * Math.PI / 90).Select(angle => radius + height * Math.Sin(lobes * angle + phase) + height * .35 * Math.Sin(Math.Floor(lobes / 2.0 + .5) * angle - phase * 1.3))];
    private static string Pc(Peer peer) => peer.Online ? Theme.Dark ? "#3DF5A0" : "Mint" : "Muted";
    /// <summary>One step per frame while Home is on screen: the field clock runs at 1 − .75 × proximity, eased so the rings settle smoothly.</summary>
    private void Step(double time)
    {
        double step = Math.Clamp(time - last, 0, .1);
        last = time;
        pull += (nearness - pull) * (1 - Math.Exp(-step / .3));
        clock += step * (1 - .75 * pull);
        Draw();
    }
    /// <summary>Draws the rings, ripples and device beats at the field clock; without motion this is the settled state.</summary>
    private void Draw()
    {
        bool moving = Motion.Enabled;
        for (int index = 0; index < Waves.Length; index++)
        {
            var wave = Waves[index];
            double cycle = Motion.Cycle(clock / wave.Morph), start = cycle < .33 ? 0 : cycle < .66 ? .33 : .66, blend = Motion.Morph.Ease((cycle - start) / ((start == .66 ? 1 : start + .33) - start));
            double turn = clock / wave.Spin * 2 * Math.PI, scale = 1 + pull * wave.Pull * (index % 3 == 1 ? -1 : 1) * .0045;
            double[] from = Shapes[index][(int)Math.Round(start / .33)], to = Shapes[index][((int)Math.Round(start / .33) + 1) % 3];
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                for (int step = 0; step <= 180; step++)
                {
                    double radius = (from[step] + (to[step] - from[step]) * blend) * scale, angle = step * Math.PI / 90 + turn;
                    Point point = new(Middle + radius * Math.Cos(angle), Middle + radius * Math.Sin(angle));
                    if (step == 0)
                        context.BeginFigure(point, false);
                    else
                        context.LineTo(point);
                }
                context.EndFigure(true);
            }
            lines[index].Data = glows[index].Data = geometry;
            // Shimmer between .55 and 1 every 7 s, each ring 0.6 s ahead of the previous one.
            lines[index].Opacity = glows[index].Opacity = moving ? .55 + .45 * Motion.InOut.Ease(Motion.Triangle(Motion.Cycle((clock + .6 * index) / 7))) : 1;
        }
        for (int index = 0; index < ripples.Length; index++)
        {
            double cycle = Motion.Cycle((clock + 6 * index) / 12), grow = moving ? .6 + 1.5 * Motion.Ripple.Ease(cycle) : 1;
            ripples[index].Scale.ScaleX = ripples[index].Scale.ScaleY = grow;
            ripples[index].Ripple.Opacity = !moving ? index == 0 ? .55 : 0 : cycle < .18 ? .55 * Motion.Ripple.Ease(cycle / .18) : .55 * (1 - Motion.Ripple.Ease((cycle - .18) / .82));
        }
        // Dots blink (.4 to 1, scale .85 to 1) with a radar ping on the same 3.8 s beat; each device starts at a different point.
        foreach (Blink blink in blinks)
        {
            double cycle = Motion.Cycle((clock + blink.Offset) / 3.8), beat = Motion.InOut.Ease(Motion.Triangle(cycle)), ping = Motion.EaseOut.Ease(Math.Min(1, cycle / .7));
            blink.Dot.Opacity = moving ? .4 + .6 * beat : 1;
            blink.Beat.ScaleX = blink.Beat.ScaleY = moving ? .85 + .15 * beat : 1;
            blink.Ping.Opacity = !moving ? .45 : cycle < .7 ? .65 * (1 - ping) : 0;
            blink.Spread.ScaleX = blink.Spread.ScaleY = moving ? .6 + 2 * ping : 1.2;
        }
    }
    public void Refresh()
    {
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        activity.Content = speed > 0 ? C.Small($"{speed / 1048576:0.0} MB/s transferring · View", () => app.Navigate("Transfers")) : null;
        fillSheet?.Invoke();
        List<Spot> spots = [.. app.Engine.Peers.Where(p => !p.Blocked && (p.Paired || p.Online)).OrderByDescending(p => p.Paired).ThenByDescending(p => p.Online).ThenBy(p => p.Name).Take(8).Select(p => new Spot(p.Fingerprint, p.Name, Pc(p), p, null)),
            .. app.Engine.Browser.Devices.Take(6).Select(b => new Spot("browser:" + b.Handle, b.Label, "Phone", null, b))];
        // Transfers refresh Home many times a second; the devices are rebuilt only when they change, so hover cards and beats carry on.
        string signature = string.Join("|", spots.Select(s => $"{s.Key}/{s.Name}/{s.Color}/{s.Peer?.Paired}/{s.Peer?.Address}/{s.Browser?.Approved}/{s.Browser?.Address}/{s.Browser?.ConnectedUtc.Ticks}"));
        if (signature == deviceSignature)
            return;
        deviceSignature = signature;
        devices.Children.Clear();
        blinks.Clear();
        cards.Clear();
        if (spots.Count == 0)
        {
            TextBlock title = C.Text("Open hopo on your other computer", 14, bold: true), detail = C.Text("Use the same home Wi-Fi. It will appear here automatically.", 12, "Muted");
            title.TextAlignment = detail.TextAlignment = TextAlignment.Center;
            StackPanel actions = C.Inline(C.Small("Find a device", () => app.Dialogs.Pair(), true), C.Small("Phone & browser", () => app.Navigate("Phone & browser")));
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            Border card = C.Card(C.Stack(C.Space(title, 4), C.Space(detail, 14), actions), 18);
            card.Width = 320;
            card.Background = Theme.Brush("Menu");
            devices.Children.Add(Place(card, Middle - 160, 418));
        }
        for (int index = 0; index < spots.Count; index++)
        {
            double radius = Orbits[index % 2], angle = (-140 + index * 360.0 / spots.Count + index % 2 * 11) * Math.PI / 180;
            Blip(spots[index], index, Middle + radius * Math.Cos(angle), Middle + radius * Math.Sin(angle));
        }
        Draw();
    }
    private void Pin(string? key)
    {
        open = key;
        foreach (Action update in cards)
            update();
    }
    private void Blip(Spot spot, int index, double x, double y)
    {
        bool right = x >= Middle;
        ScaleTransform beat = new(), spread = new(1.2, 1.2);
        Ellipse ping = new()
        {
            Width = 20,
            Height = 20,
            Stroke = Theme.Brush(spot.Color),
            StrokeThickness = 1,
            RenderTransform = spread
        };
        Border dot = new()
        {
            Width = 10,
            Height = 10,
            CornerRadius = new(5),
            Background = Theme.Brush(spot.Color),
            BoxShadow = new BoxShadows(new BoxShadow { Spread = 4, Color = Theme.Color(spot.Color + "/15") }, [new BoxShadow { Blur = 16, Color = Theme.Color(spot.Color + "/75") }]),
            RenderTransform = beat
        };
        blinks.Add(new(dot, beat, ping, spread, index * 1.05));
        Button blip = C.Ghost(new Button { Content = new Panel { Width = 44, Height = 44, Children = { ping, dot } }, Width = 44, Height = 44, Padding = new(0), CornerRadius = new(22), BorderThickness = new(0), Classes = { "plain" } });
        AutomationProperties.SetName(blip, spot.Name + (spot.Browser != null ? ", through the browser" : ", on your network"));
        Border tag = new()
        {
            Child = C.Medium(spot.Name, 12),
            Padding = new(9, 5),
            CornerRadius = new(9),
            MaxWidth = 180,
            Background = Theme.Brush(Theme.Dark ? "#0A0A12/50" : "Glass"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            IsHitTestVisible = false
        };
        Canvas.SetTop(tag, y - 14);
        if (right)
            Canvas.SetLeft(tag, x + 18);
        else
            Canvas.SetRight(tag, Size - x + 18);
        // Hover or keyboard focus swaps the name tag for the device card, which rises 8 px from 97%. A click pins it for touch screens.
        Border card = Detail(spot, x, y, right);
        card.ZIndex = 10;
        Motion.Ease(tag, .25);
        Motion.Ease(card, .3, .45);
        void Update()
        {
            bool shown = open == spot.Key || blip.IsPointerOver || card.IsPointerOver || blip.IsKeyboardFocusWithin || card.IsKeyboardFocusWithin;
            card.Opacity = shown ? 1 : 0;
            card.IsHitTestVisible = shown;
            card.RenderTransform = Motion.Transform(shown ? "translateY(0px) scale(1)" : "translateY(8px) scale(0.97)");
            tag.Opacity = shown ? 0 : 1;
        }
        // Leaving waits a moment, so the pointer can cross the gap between the dot and its card.
        void Later() => DispatcherTimer.RunOnce(Update, TimeSpan.FromMilliseconds(160));
        blip.PointerEntered += (_, _) => Update();
        card.PointerEntered += (_, _) => Update();
        blip.PointerExited += (_, _) => Later();
        card.PointerExited += (_, _) => Later();
        blip.GotFocus += (_, _) => Update();
        card.GotFocus += (_, _) => Update();
        blip.LostFocus += (_, _) => Dispatcher.UIThread.Post(Update);
        card.LostFocus += (_, _) => Dispatcher.UIThread.Post(Update);
        blip.Click += (_, _) => Pin(open == spot.Key ? null : spot.Key);
        cards.Add(Update);
        Update();
        devices.Children.Add(Place(blip, x - 22, y - 22));
        devices.Children.Add(tag);
        devices.Children.Add(card);
    }
    /// <summary>The small glass card for one device: name and status, address and trust, and its actions (Send files and Text open the Home panels).</summary>
    private Border Detail(Spot spot, double x, double y, bool right)
    {
        Peer? peer = spot.Peer;
        BrowserDevice? browser = spot.Browser;
        bool online = browser != null || peer!.Online;
        string chip = browser != null ? browser.Approved ? "Browser" : "Waiting" : online ? "Online" : "Offline";
        string address = browser != null ? browser.Address + " · connected " + browser.ConnectedUtc.ToLocalTime().ToString("HH:mm") : online ? "LAN · " + peer!.Address : "Offline · open hopo on that computer";
        string trust = browser != null ? browser.Approved ? "Approved on this " + Shell.Platform : "Waiting for your Allow" : peer!.Paired ? "Paired · key verified" : "Not paired yet";
        StackPanel actions = C.Inline();
        actions.Spacing = 6;
        if (browser != null)
        {
            actions.Children.Add(browser.Approved ? C.Small("Send files", () => { app.SelectedBrowserHandle = browser.Handle; Open("send"); }, true) : C.Small("Allow", () => app.Engine.Browser.Approve(browser.Handle), true));
            actions.Children.Add(C.Ghost(C.Small("Disconnect", async () =>
            {
                if (!await app.Dialogs.ConfirmAsync("Disconnect browser", "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect"))
                    return;
                open = null;
                app.Engine.Browser.Disconnect(browser.Handle);
            })));
        }
        else if (peer!.Paired)
        {
            actions.Children.Add(C.Small("Send files", () => { app.SelectedPeerKey = peer.Fingerprint; app.SelectedBrowserHandle = null; Open("send"); }, true));
            actions.Children.Add(C.Small("Text", () => { noteKey = peer.Fingerprint; Open("text"); }));
        }
        else
            actions.Children.Add(C.Small("Connect", () => app.Dialogs.Pair(peer), true));
        TextBlock name = C.Text(spot.Name, 13, bold: true);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        Border badge = C.Badge(chip, spot.Color, spot.Color + "/14");
        badge.MinHeight = 20;
        badge.Margin = new(8, 0, 0, 0);
        DockPanel top = new();
        DockPanel.SetDock(badge, Dock.Right);
        top.Children.Add(badge);
        top.Children.Add(name);
        Border card = C.Card(C.Stack(C.Space(top, 10), C.Space(C.Text(address, 11, "Muted"), 3), C.Space(C.Text(trust, 11, "Muted"), 12), actions), new Thickness(14), 16);
        card.Width = CardWidth;
        card.Background = Theme.Brush("Menu");
        card.BoxShadow = Theme.Shadow(20, 50, 0.5);
        return Place(card, Math.Clamp(right ? x - 30 - CardWidth : x + 30, 0, Size - CardWidth), Math.Clamp(y - 26, 0, Size - 160));
    }
    /// <summary>Opens one dock panel (or closes it with null). It grows out of its button; the button shows its "on" state.</summary>
    private void Open(string? key)
    {
        sheet = key;
        Pin(null);
        foreach ((string name, Button button) in dock)
        {
            bool on = name == key;
            button.Classes.Set("on", on);
            if (name == "send")
                Theme.SetButton(button.Resources, on ? "#FFC567" : "Accent", "OnAccent", on ? "#FFC567" : "Accent", "#FFC567");
            else
                Theme.SetButton(button.Resources, on ? "Text/14" : "Glass", "Text", on ? "Text/26" : "GlassBorder", on ? "Text/14" : "Text/8");
        }
        clickAway.IsVisible = key != null;
        sheetHost.Children.Clear();
        fillSheet = null;
        if (key == null)
            return;
        StackPanel body = key switch
        {
            "send" => SendSheet(),
            "folder" => FolderSheet(),
            "text" => TextSheet(),
            _ => PhoneSheet()
        };
        body.Spacing = 14;
        Border card = C.Card(body, new Thickness(18));
        card.Width = 440;
        card.Background = Theme.Brush(Theme.Dark ? "#0E0E18/92" : "Menu");
        card.BoxShadow = Theme.Shadow(30, 80, 0.5);
        card.RenderTransformOrigin = new RelativePoint(key switch
        {
            "send" => .14,
            "folder" => .4,
            "text" => .61,
            _ => .84
        }, 1.12, RelativeUnit.Relative);
        if (key == "send")
        {
            // Files dropped on this panel join the selection and Home stays open.
            DragDrop.SetAllowDrop(card, true);
            card.AddHandler(DragDrop.DropEvent, async (_, e) =>
            {
                e.Handled = true;
                string[] paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
                if (paths.Length > 0)
                    await app.AddSelectionsAsync(paths, true);
            });
        }
        sheetHost.Children.Add(card);
        fillSheet?.Invoke();
        Motion.Pop(card);
        foreach (Control child in body.Children)
            Motion.Rise(child, 6, .45, .14, Motion.EaseOut);
    }
    private StackPanel Sheet(string title, params Control[] parts)
    {
        Button close = C.IconButton("close", () => Open(null), "Close panel", true);
        TextBlock heading = C.Text(title, 15, bold: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        DockPanel head = new();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(heading);
        return C.Stack([head, .. parts]);
    }
    private static StackPanel Labelled(string label, Control content) => C.Stack(C.Space(C.Text(label, 12, "Muted"), 8), content);
    /// <summary>A "Send to" or access chip; the chosen one is violet.</summary>
    private static Button Chip(string label, string? dot, bool chosen, Action choose)
    {
        TextBlock text = C.Medium(label, 13, chosen ? "Text" : "Text/80");
        text.VerticalAlignment = VerticalAlignment.Center;
        Button chip = new()
        {
            Content = dot == null ? text : C.Inline(C.Dot(dot, 7), text),
            MinHeight = 40,
            Padding = new(14, 0),
            CornerRadius = new(10),
            BorderThickness = new(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 8, 8)
        };
        Theme.SetButton(chip.Resources, chosen ? "Phone/14" : "Text/4", "Text", chosen ? "Phone/70" : "Text/12", chosen ? "Phone/18" : "Text/8");
        AutomationProperties.SetName(chip, label + (chosen ? ", selected" : ""));
        chip.Click += (_, _) => choose();
        return chip;
    }
    private static Grid Drop(TextBlock text, Button action)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        action.Margin = new(12, 0, 0, 0);
        DockPanel row = new();
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        row.Children.Add(text);
        Grid area = C.Dashed(row);
        ((Border)area.Children[1]).Padding = new(16);
        return area;
    }
    private Target[] Targets() => [.. app.Engine.Peers.Where(p => p.Paired && !p.Blocked).Select(p => new Target(p.Name, Pc(p), p, null)), .. app.Engine.Browser.Devices.Where(d => d.Approved && app.Engine.Browser.Running).Select(d => new Target(d.Label, "Phone", null, d))];
    private Target? Chosen(Target[] targets) => targets.FirstOrDefault(t => t.Browser != null && t.Browser.Handle == app.SelectedBrowserHandle) ?? targets.FirstOrDefault(t => t.Peer != null && t.Peer.Fingerprint == app.SelectedPeerKey) ?? targets.FirstOrDefault();
    private StackPanel SendSheet()
    {
        TextBlock summary = C.Text("", 13, "Muted");
        WrapPanel targets = new();
        Button send = C.AsyncButton("Send", async () =>
        {
            if (Chosen(Targets()) is not { } target || app.Selection.Count == 0)
                return;
            await app.SendToAsync(target.Peer, target.Browser, false);
            Open(null);
        }, true);
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        string shown = "";
        fillSheet = () =>
        {
            int files = app.Selection.Sum(item => item.Files);
            summary.Text = app.Selection.Count == 0 ? "Drop files here" : string.Join(", ", app.Selection.Take(2).Select(item => item.Name)) + (app.Selection.Count > 2 ? $" +{app.Selection.Count - 2}" : "") + $" · {files} file{(files == 1 ? "" : "s")} · {Format.Bytes(app.Selection.Sum(item => item.Size))}";
            summary.Foreground = Theme.Brush(app.Selection.Count == 0 ? "Muted" : "Text");
            Target[] all = Targets();
            Target? chosen = Chosen(all);
            send.IsEnabled = app.Selection.Count > 0 && chosen != null;
            string next = string.Join("|", all.Select(t => t.Label + t.Color + (ReferenceEquals(t, chosen) ? "*" : "")));
            if (next == shown)
                return;
            shown = next;
            targets.Children.Clear();
            if (all.Length == 0)
                targets.Children.Add(C.Small("Connect a device", () => app.Dialogs.Pair(), true));
            foreach (Target target in all)
                targets.Children.Add(Chip(target.Label, target.Color, ReferenceEquals(target, chosen), () =>
                {
                    app.SelectedBrowserHandle = target.Browser?.Handle;
                    if (target.Peer != null)
                        app.SelectedPeerKey = target.Peer.Fingerprint;
                    fillSheet?.Invoke();
                }));
        };
        return Sheet("Send files", Drop(summary, C.AsyncButton("Choose files", async () => await app.AddSelectionsAsync(await app.PickFilesAsync("Choose files to send"), true), icon: "plus")), Labelled("Send to", targets), send);
    }
    private StackPanel FolderSheet()
    {
        TextBlock chosen = C.Text("", 13, "Muted");
        WrapPanel options = new();
        Button start = C.Button("Start sharing", () =>
        {
            if (folder == null)
                return;
            SharedSpace space = app.Engine.Browser.Spaces.Add(folder);
            if (access == SpaceAccess.ReadAndUpload)
                app.Engine.Browser.Spaces.Update(space.Id, access, true);
            folder = null;
            Open(null);
            app.Tell(access == SpaceAccess.ReadAndUpload ? "Shared folder created with Read & upload. Paired computers can browse it and add files." : "Read-only shared folder created. Paired computers can browse it in Shared folders.");
        }, true);
        Button all = C.Link("All shared folders", () =>
        {
            app.Navigate("Shared Spaces");
            return Task.CompletedTask;
        });
        all.VerticalAlignment = VerticalAlignment.Center;
        DockPanel actions = new();
        DockPanel.SetDock(start, Dock.Right);
        actions.Children.Add(start);
        actions.Children.Add(new Border { Child = all, HorizontalAlignment = HorizontalAlignment.Left });
        SpaceAccess? shown = null;
        fillSheet = () =>
        {
            chosen.Text = folder ?? "No folder chosen";
            chosen.Foreground = Theme.Brush(folder == null ? "Muted" : "Text");
            start.IsEnabled = folder != null;
            if (shown == access)
                return;
            shown = access;
            options.Children.Clear();
            foreach ((SpaceAccess value, string label) in new[] { (SpaceAccess.ReadOnly, "Read only"), (SpaceAccess.ReadAndUpload, "Read & upload") })
                options.Children.Add(Chip(label, null, access == value, () => { access = value; fillSheet?.Invoke(); }));
        };
        return Sheet("Share a folder", Drop(chosen, C.AsyncButton("Choose folder", async () =>
        {
            string[] picked = await app.PickFoldersAsync("Choose a folder to share");
            if (picked.Length > 0)
                folder = picked[0];
            fillSheet?.Invoke();
        }, icon: "folder")), Labelled("Access", options), actions);
    }
    private StackPanel TextSheet()
    {
        WrapPanel targets = new();
        Peer[] Paired() => [.. app.Engine.Peers.Where(p => p.Paired && !p.Blocked)];
        // A PC chosen on its device card comes first; otherwise the first paired PC, or this computer when none is paired.
        noteKey ??= Paired().FirstOrDefault()?.Fingerprint ?? "";
        Button share = C.AsyncButton("Share", async () =>
        {
            string content = SharedNotes.Validate(message.Text);
            if (Paired().FirstOrDefault(p => p.Fingerprint == noteKey) is { } peer)
                await app.Engine.SendNoteAsync(peer, content);
            else
                app.Engine.Browser.Notes.Add(content, "This " + Shell.Platform, local: true);
            if ((message.Text ?? "").Trim() == content)
                message.Text = "";
            Open(null);
            app.Tell("Text shared. Find it in Text & links on the receiving computer or its browser page.");
        }, true);
        share.HorizontalAlignment = HorizontalAlignment.Stretch;
        string shown = "";
        fillSheet = () =>
        {
            Peer[] peers = Paired();
            string next = string.Join("|", peers.Select(p => p.Fingerprint + p.Name)) + "#" + noteKey;
            if (next == shown)
                return;
            shown = next;
            targets.Children.Clear();
            foreach (Peer peer in peers)
                targets.Children.Add(Chip(peer.Name, null, noteKey == peer.Fingerprint, () => { noteKey = peer.Fingerprint; fillSheet?.Invoke(); }));
            targets.Children.Add(Chip("This " + Shell.Platform + " (for my phone)", null, !peers.Any(p => p.Fingerprint == noteKey), () => { noteKey = ""; fillSheet?.Invoke(); }));
        };
        return Sheet("Share text or a link", C.Stack(C.Space(C.Text("Message", 12, "Muted"), 6), message), Labelled("Send to", targets), share);
    }
    private StackPanel PhoneSheet()
    {
        ContentControl body = new();
        string? shown = null;
        fillSheet = () =>
        {
            bool on = app.Engine.Browser.Running;
            string url = on ? app.Engine.Browser.IpUrl : "";
            if (url == shown)
                return;
            shown = url;
            Control code;
            if (on)
                code = PhonePage.Qr(url, 100);
            else
            {
                Grid placeholder = C.Dashed(C.Icon("phone", 30, "Muted"));
                placeholder.Width = placeholder.Height = 112;
                code = placeholder;
            }
            code.Margin = new(0, 0, 16, 0);
            TextBlock hint = C.Text("Same Wi‑Fi, then scan with your camera.", 13, "Muted"), address = C.Text(on ? url : "Browser sharing is off", 12, "Muted");
            address.FontFamily = new FontFamily("monospace");
            StackPanel text = C.Stack(C.Space(hint, 8), address);
            text.VerticalAlignment = VerticalAlignment.Center;
            DockPanel row = new();
            DockPanel.SetDock(code, Dock.Left);
            row.Children.Add(code);
            row.Children.Add(text);
            if (on)
            {
                body.Content = row;
                return;
            }
            Button start = C.Button("Start browser sharing", () =>
            {
                app.StartBrowserSharing();
                fillSheet?.Invoke();
            }, true, "phone");
            start.HorizontalAlignment = HorizontalAlignment.Stretch;
            body.Content = C.Stack(C.Space(row, 14), start);
        };
        return Sheet("Open on your phone", body);
    }
}

internal sealed class SendPage : IPage
{
    private sealed record Recipient(string Label, string Detail, string Color, Peer? Peer, BrowserDevice? Browser);
    private readonly Shell app;
    private readonly StackPanel files = new(), recipient = new();
    private readonly TextBlock totals = C.Text("", 13, "Muted");
    public Control View
    {
        get;
    }
    public SendPage(Shell app)
    {
        this.app = app;
        StackPanel tools = C.Inline(C.AsyncButton("Add files", app.PickFilesAsync, icon: "plus"), C.AsyncButton("Add folder", app.PickFoldersAsync, icon: "folder"), C.Ghost(C.Button("Clear", () => { app.Selection.Clear(); Refresh(); })));
        tools.VerticalAlignment = VerticalAlignment.Center;
        DockPanel top = new();
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Add(tools);
        top.Children.Add(C.Stack(C.Text("Selected", 16, bold: true), C.Space(totals, 0, 4)));
        StackPanel drop = C.Inline(C.Icon("upload", 18, "Muted"), C.Text("Drop more files or folders here", 13, "Muted"));
        drop.Spacing = 10;
        drop.HorizontalAlignment = HorizontalAlignment.Center;
        // The whole window accepts dropped files; this area shows where to drop them.
        StackPanel left = C.Stack(top, new Border { BorderBrush = Theme.Brush("Text/6"), BorderThickness = new(0, 1, 0, 0), Padding = new(0, 8, 0, 0), Margin = new(0, 16), Child = files }, C.Dashed(drop));
        Button phone = C.Link("Or share with a phone or browser", async () =>
        {
            if (app.Selection.Count > 0)
                await Task.Run(() => app.Engine.Browser.ShareAsync(app.Selection.Select(item => item.Path).ToArray()));
            app.Navigate("Phone & browser");
        });
        phone.HorizontalAlignment = HorizontalAlignment.Center;
        StackPanel right = C.Stack(C.Card(C.Stack(recipient, C.Space(phone, 0, 10))), C.Space(C.Card(C.Stack(C.Point("The recipient stays in control", "They see your name, file count and size, then accept or decline."),
            C.Space(C.Point("Original bytes, verified", "Folders keep their structure. Every file is checked before it's finalized.", "check"), 0, 16)), 20), 0, 16));
        View = C.Stack(C.Header("Share", "Send files", "Pick files, then a device. They approve before anything moves."), C.Columns(C.Card(left), right, 340));
    }
    private static string Count(int value, string singular) => value + " " + singular + (value == 1 ? "" : "s");
    public void Refresh()
    {
        files.Children.Clear();
        totals.Text = $"{Count(app.Selection.Count, "item")} · {Count(app.Selection.Sum(s => s.Files), "file")} · {Format.Bytes(app.Selection.Sum(s => s.Size))}";
        SelectionItem[] visible = app.Selection.Where(s => s.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visible.Length == 0)
            files.Children.Add(C.Space(C.Text(app.Selection.Count == 0 ? "Add files or folders, or drop them anywhere in this window." : "No selection matches your search.", 13, "Muted"), 14, 12));
        foreach (SelectionItem item in visible)
        {
            bool folder = item.Folders > 0;
            Border tile = C.Tile(folder ? "folder" : "file", folder ? "Primary" : "Mint");
            tile.Margin = new(0, 0, 14, 0);
            Button remove = C.IconButton("close", () => { app.Selection.Remove(item); Refresh(); }, "Remove " + item.Name, true);
            StackPanel text = C.Stack(C.Medium(item.Name, 14), C.Text(item.Detail, 12, "Muted"));
            text.VerticalAlignment = VerticalAlignment.Center;
            DockPanel row = new()
            {
                Margin = new(14, 6, 0, 6)
            };
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Add(tile);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(text);
            files.Children.Add(row);
        }
        recipient.Children.Clear();
        recipient.Children.Add(C.Heading("Send to"));
        Recipient[] targets = [.. app.Engine.Peers.Where(p => p.Paired && !p.Blocked).Select(p => new Recipient(p.Name, p.Online ? "Online · LAN" : "Offline", p.Online ? "Mint" : "Muted", p, null)),
            .. app.Engine.Browser.Devices.Where(d => d.Approved && app.Engine.Browser.Running).Select(d => new Recipient(d.Label, "Phone / browser · " + d.Address, "Phone", null, d))];
        if (targets.Length == 0)
        {
            recipient.Children.Add(C.Space(C.Text("Connect another computer once, then it will be remembered for future transfers.", 12, "Muted"), 18));
            Button connect = C.Button("Connect a device", () => app.Dialogs.Pair(), true, "pair");
            connect.HorizontalAlignment = HorizontalAlignment.Stretch;
            recipient.Children.Add(connect);
            return;
        }
        Recipient chosen = targets.FirstOrDefault(t => t.Browser != null && t.Browser.Handle == app.SelectedBrowserHandle) ?? targets.FirstOrDefault(t => t.Peer != null && t.Peer.Fingerprint == app.SelectedPeerKey) ?? targets[0];
        foreach (Recipient target in targets)
            recipient.Children.Add(C.Space(Choice(target, ReferenceEquals(target, chosen)), 10));
        recipient.Children.Add(C.Space(C.Text(chosen.Browser != null ? "Files are listed on that phone's page only, under Files from your PC. Other phones do not see them." : "The public key is checked again when connecting. Offline devices may need receiving switched on.", 11, "Muted"), 14, 4));
        Button send = C.AsyncButton("Send securely", () => app.SendToAsync(chosen.Peer, chosen.Browser), true, "lock");
        send.IsEnabled = app.Selection.Count > 0;
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        send.MinHeight = 48;
        send.FontSize = 14;
        recipient.Children.Add(send);
        recipient.Children.Add(C.Space(C.Text("Duplicate handling is chosen by the recipient. Existing files are never silently overwritten.", 11, "Muted"), 0, 12));
    }
    /// <summary>One recipient as a radio row: the selected one carries the amber outline.</summary>
    private Button Choice(Recipient target, bool selected)
    {
        Panel radio = new()
        {
            Width = 18,
            Height = 18,
            Margin = new(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        radio.Children.Add(new Ellipse { Stroke = Theme.Brush(selected ? "Accent" : "Muted"), StrokeThickness = 1.5 });
        if (selected)
            radio.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.Brush("Accent") });
        Ellipse dot = C.Dot(target.Color);
        dot.Margin = new(12, 0, 0, 0);
        StackPanel text = C.Stack(C.Medium(target.Label, 14), C.Text(target.Detail, 12, "Muted"));
        text.VerticalAlignment = VerticalAlignment.Center;
        DockPanel row = new();
        DockPanel.SetDock(radio, Dock.Left);
        row.Children.Add(radio);
        DockPanel.SetDock(dot, Dock.Right);
        row.Children.Add(dot);
        row.Children.Add(text);
        Button button = new()
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new(14, 12),
            CornerRadius = new(14),
            BorderThickness = new(1)
        };
        Theme.SetButton(button.Resources, selected ? "Accent/6" : C.Clear, "Text", selected ? "Accent/50" : "GlassBorder", selected ? "Accent/10" : "Text/4");
        AutomationProperties.SetName(button, target.Label + (selected ? ", selected" : ""));
        button.Click += (_, _) =>
        {
            app.SelectedBrowserHandle = target.Browser?.Handle;
            if (target.Peer != null)
                app.SelectedPeerKey = target.Peer.Fingerprint;
            Refresh();
        };
        return button;
    }
}
