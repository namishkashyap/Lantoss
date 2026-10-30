using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lantoss.Desktop;

/// <summary>The nearby radar: this computer in the middle, paired PCs (mint) and phones or browsers (purple) as dots on the rings.</summary>
internal sealed class HomePage : IPage
{
    private const double Size = 640, Middle = 320, CardWidth = 236;
    private static readonly double[] Orbits = [170, 235, 300];
    private sealed record Spot(string Key, string Name, string Color, Peer? Peer, BrowserDevice? Browser);
    private readonly Shell app;
    private readonly Canvas stage = new() { Width = Size, Height = Size, Background = Brushes.Transparent }, devices = new() { Width = Size, Height = Size };
    private readonly ContentControl activity = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private string? open;
    public Control View
    {
        get;
    }
    public HomePage(Shell app)
    {
        this.app = app;
        stage.Children.Add(Ring(300, "Text/7"));
        stage.Children.Add(Ring(235, "Text/9"));
        stage.Children.Add(Ring(170, "Text/11"));
        stage.Children.Add(Ring(100, "Text/14", "Text/3"));
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
        StackPanel buttons = C.Inline(C.Button("Send files", () => app.Navigate("Send"), true, "send"), C.Button("Share a folder", () => app.Navigate("Shared Spaces"), icon: "folder"), C.Button("Share text", () => app.Navigate("Text & links"), icon: "text"), C.Button("Open on your phone", () => app.Navigate("Phone & browser"), icon: "phone"));
        Border dock = new()
        {
            Child = buttons,
            Padding = new(8),
            CornerRadius = new(18),
            Background = Theme.Brush(Theme.Dark ? "#0C0C14/50" : "Glass"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            BoxShadow = Theme.Shadow(24, 60, 0.45),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        StackPanel foot = C.Stack(activity, dock);
        foot.Spacing = 12;
        foot.Margin = new(16, 0, 16, 24);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        Grid layout = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = Brushes.Transparent
        };
        Grid.SetRow(foot, 1);
        // Faint outer rings continue the radar to the window edges; the Viewbox would clip them, so they sit behind it.
        layout.Children.Add(new Border { Child = C.Rings(360, 820, "Ring", 364), Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        layout.Children.Add(new Viewbox { Child = stage, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(16, 8, 16, 12) });
        layout.Children.Add(foot);
        // Clicking empty space closes an open device card.
        layout.PointerPressed += (_, e) =>
        {
            if (open != null && (ReferenceEquals(e.Source, stage) || ReferenceEquals(e.Source, layout)))
            {
                open = null;
                Refresh();
            }
        };
        View = layout;
    }
    private static T Place<T>(T control, double x, double y) where T : Control
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        return control;
    }
    private static Ellipse Ring(double radius, string stroke, string? fill = null) => Place(new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Theme.Brush(stroke), StrokeThickness = 1, Fill = fill == null ? null : Theme.Brush(fill), IsHitTestVisible = false }, Middle - radius, Middle - radius);
    public void Refresh()
    {
        devices.Children.Clear();
        List<Spot> spots = [.. app.Engine.Peers.Where(p => !p.Blocked && (p.Paired || p.Online)).OrderByDescending(p => p.Paired).ThenByDescending(p => p.Online).ThenBy(p => p.Name).Take(8).Select(p => new Spot(p.Fingerprint, p.Name, p.Online ? "Mint" : "Muted", p, null)),
            .. app.Engine.Browser.Devices.Take(6).Select(b => new Spot("browser:" + b.Handle, b.Label, "Phone", null, b))];
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
            double radius = Orbits[index % 3], angle = (-140 + index * 360.0 / spots.Count + index % 3 * 11) * Math.PI / 180;
            Blip(spots[index], Middle + radius * Math.Cos(angle), Middle + radius * Math.Sin(angle));
        }
        // The open card is drawn last so it sits above every other dot and name.
        if (devices.Children.OfType<Border>().FirstOrDefault(child => child.Tag is "detail") is { } detailCard)
        {
            devices.Children.Remove(detailCard);
            devices.Children.Add(detailCard);
        }
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        activity.Content = speed > 0 ? C.Small($"{speed / 1048576:0.0} MB/s transferring · View", () => app.Navigate("Transfers")) : null;
    }
    private void Blip(Spot spot, double x, double y)
    {
        bool shown = open == spot.Key, right = x >= Middle;
        Panel glyph = new()
        {
            Width = 44,
            Height = 44
        };
        glyph.Children.Add(new Ellipse { Width = 24, Height = 24, Stroke = Theme.Brush(spot.Color + "/45"), StrokeThickness = 1 });
        glyph.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = Theme.Brush(spot.Color), BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Spread = 1, Color = Theme.Color(spot.Color + "/70") }) });
        Button blip = C.Ghost(new Button { Content = glyph, Width = 44, Height = 44, Padding = new(0), CornerRadius = new(22), BorderThickness = new(0) });
        AutomationProperties.SetName(blip, spot.Name + (spot.Browser != null ? ", through the browser" : ", on your network"));
        blip.Click += (_, _) =>
        {
            open = shown ? null : spot.Key;
            Refresh();
        };
        devices.Children.Add(Place(blip, x - 22, y - 22));
        if (shown)
        {
            devices.Children.Add(Detail(spot, x, y, right));
            return;
        }
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
        devices.Children.Add(tag);
    }
    /// <summary>The small glass card for one device, wired to the same actions as Devices and Send.</summary>
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
            actions.Children.Add(browser.Approved ? C.Small("Send files", () => { app.SelectedBrowserHandle = browser.Handle; app.Navigate("Send"); }, true) : C.Small("Allow", () => app.Engine.Browser.Approve(browser.Handle), true));
            actions.Children.Add(C.Small("Disconnect", async () =>
            {
                if (!await app.Dialogs.ConfirmAsync("Disconnect browser", "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect"))
                    return;
                open = null;
                app.Engine.Browser.Disconnect(browser.Handle);
            }));
        }
        else if (peer!.Paired)
        {
            actions.Children.Add(C.Small("Send files", () => { app.SelectedPeerKey = peer.Fingerprint; app.SelectedBrowserHandle = null; app.Navigate("Send"); }, true));
            actions.Children.Add(C.Small("Text", () => { app.NotePeerKey = peer.Fingerprint; app.Navigate("Text & links"); }));
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
        Border card = new()
        {
            Tag = "detail",
            Width = CardWidth,
            Padding = new(14),
            CornerRadius = new(16),
            Background = Theme.Brush("Menu"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            BoxShadow = Theme.Shadow(20, 50, 0.5),
            Child = C.Stack(C.Space(top, 10), C.Space(C.Text(address, 11, "Muted"), 3), C.Space(C.Text(trust, 11, "Muted"), 12), actions)
        };
        return Place(card, Math.Clamp(right ? x - 30 - CardWidth : x + 30, 0, Size - CardWidth), Math.Clamp(y - 26, 0, Size - 160));
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
        Button send = C.AsyncButton("Send securely", async () =>
        {
            if (chosen.Peer != null)
            {
                app.SendSelected(chosen.Peer);
                return;
            }
            if (chosen.Browser is not BrowserDevice device || app.Selection.Count == 0)
                return;
            string[] paths = app.Selection.Select(item => item.Path).ToArray();
            await Task.Run(() => app.Engine.Browser.ShareAsync(paths, default, device.Handle));
            app.Tell("Sent to " + device.Label + ". It appears on that phone's page under Files from your PC.");
        }, true, "lock");
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
