using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace Lantoss.UI;

internal sealed class HomePage : IPage
{
    // The radar is drawn on a fixed 640 px stage and scaled down on small windows, like the design's nearby field.
    private const double Stage = 640, Middle = Stage / 2;
    private readonly AppShell app;
    private readonly Canvas radar = new() { Width = Stage, Height = Stage };
    private readonly StackPanel empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 6) };
    private readonly TextBlock status = C.Text("", 12, "Muted");
    public FrameworkElement View
    {
        get;
    }
    public HomePage(AppShell app)
    {
        this.app = app;
        Button send = C.Button("Send files", () => app.Navigate("Send"), true, "send"), folder = C.Button("Share a folder", () => app.Navigate("Shared Spaces"), icon: "folder"), text = C.Button("Share text", () => app.Navigate("Text & links"), icon: "file"), phone = C.Button("Open on your phone", () => app.Navigate("Phone & browser"), icon: "phone");
        foreach (Button item in new[] { folder, text, phone })
            item.Margin = new(8, 0, 0, 0);
        Border dock = C.Card(C.Inline(send, folder, text, phone), 8);
        dock.CornerRadius = new(20);
        dock.HorizontalAlignment = HorizontalAlignment.Center;
        status.HorizontalAlignment = HorizontalAlignment.Center;
        Grid field = new()
        {
            MinHeight = 560
        };
        field.Children.Add(new Viewbox { Child = radar, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = Stage });
        field.Children.Add(empty);
        Grid layout = new()
        {
            AllowDrop = true
        };
        layout.Background = Brushes.Transparent;
        layout.RowDefinitions.Add(new()
        {
            Height = new(1, GridUnitType.Star)
        });
        layout.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        layout.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        layout.Children.Add(field);
        Grid.SetRow(dock, 1);
        layout.Children.Add(C.Space(dock, 10, 12));
        Grid.SetRow(status, 2);
        layout.Children.Add(status);
        // Dropping files anywhere on Home starts a send, as the old drop area did.
        AutomationProperties.SetName(layout, "Drop files or folders to prepare a transfer");
        layout.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        layout.Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { await app.AddSelectionsAsync(paths); app.Navigate("Send"); } e.Handled = true; };
        View = layout;
    }
    public static Border Dropzone(AppShell app)
    {
        Grid layout = new()
        {
            MinHeight = 200
        };
        Rectangle outline = new()
        {
            RadiusX = 13,
            RadiusY = 13,
            StrokeThickness = 1.2,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false
        };
        outline.SetResourceReference(Shape.StrokeProperty, "Primary");
        layout.Children.Add(outline);
        FrameworkElement icon = C.Icon("upload", 39);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock title = C.Text("Drop files here", 21, bold: true);
        title.TextAlignment = TextAlignment.Center;
        TextBlock subtitle = C.Text("Files and folders stay on your home network.", 12, "Muted");
        subtitle.TextAlignment = TextAlignment.Center;
        Button files = C.Button("Choose files", app.PickFiles, true, "plus"), folders = C.Button("Choose folder", app.PickFolders);
        folders.Margin = new(10, 0, 0, 0);
        StackPanel buttons = C.Inline(files, folders);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        StackPanel body = C.Stack(C.Space(icon, 12), C.Space(title, 7), C.Space(subtitle, 18), buttons);
        body.VerticalAlignment = VerticalAlignment.Center;
        body.Margin = new(24);
        layout.Children.Add(body);
        Border drop = new()
        {
            Child = layout,
            CornerRadius = new(13),
            AllowDrop = true
        };
        drop.SetResourceReference(Border.BackgroundProperty, "PrimarySoft");
        AutomationProperties.SetName(drop, "Drop files or folders to prepare a transfer");
        drop.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        drop.Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await app.AddSelectionsAsync(paths); e.Handled = true; };
        return drop;
    }
    public void Refresh()
    {
        radar.Children.Clear();
        empty.Children.Clear();
        foreach (double size in new[] { 600.0, 470, 340 })
        {
            Ellipse ring = new()
            {
                Width = size,
                Height = size,
                StrokeThickness = 1
            };
            ring.SetResourceReference(Shape.StrokeProperty, "GlassBorder");
            Place(ring, Middle - size / 2, Middle - size / 2);
        }
        Border self = new()
        {
            Width = 150,
            Height = 150,
            CornerRadius = new(75),
            BorderThickness = new(1)
        };
        self.SetResourceReference(Border.BackgroundProperty, "Glass");
        self.SetResourceReference(Border.BorderBrushProperty, "GlassBorder");
        Ellipse beacon = new()
        {
            Width = 9,
            Height = 9,
            Margin = new(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center
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
        Place(self, Middle - 75, Middle - 75);
        Peer[] peers = app.Engine.Peers.Where(p => !p.Blocked && (p.Online || p.Paired) && p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.Online).ThenBy(p => p.Name).Take(8).ToArray();
        BrowserDevice[] browsers = app.Engine.Browser.Devices.Where(b => b.Approved && b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).Take(6).ToArray();
        int count = peers.Length + browsers.Length, index = 0;
        foreach (Peer peer in peers)
            Blip(index++, count, peer.Name, peer.Online ? "Mint" : "Muted", peer.Name + (peer.Online ? ", on Wi‑Fi" : ", offline"), Details(peer.Name, peer.Online ? "Online" : "Offline", peer.Online, "Wi‑Fi · " + peer.Address, peer.TrustLabel, peer.Paired ? C.Small("Send files", () => { app.SelectedPeerKey = peer.Fingerprint; app.Navigate("Send"); }) : C.Small("Connect", () => app.Dialogs.Pair(peer)), peer.Paired ? C.Small("Text", () => app.Navigate("Text & links")) : null));
        foreach (BrowserDevice browser in browsers)
            Blip(index++, count, browser.Label, "Phone", browser.Label + ", through the browser", Details(browser.Label, "Browser", true, "Connected · " + browser.Address, "Approved on this PC", C.Small("Send files", () => app.Navigate("Send")), C.Small("Disconnect", () => { if (MessageBox.Show(app.Window, "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect browser", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Browser.Disconnect(browser.Handle); })));
        if (count == 0)
        {
            TextBlock title = C.Text(app.Search.Length > 0 ? "No matching devices" : "Open hopo on your other PC", 15, bold: true), detail = C.Text("Use the same home Wi‑Fi. Your PCs and approved phones appear on this radar automatically.", 12, "Muted");
            title.TextAlignment = detail.TextAlignment = TextAlignment.Center;
            detail.MaxWidth = 420;
            Button find = C.Button("Find a device", () => app.Dialogs.Pair(), icon: "devices");
            find.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(C.Card(C.Stack(C.Space(title, 6), C.Space(detail, 14), find), 18));
        }
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        status.Text = speed > 0 ? $"{speed / 1048576:0.0} MB/s transferring" : "";
        status.Visibility = speed > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        radar.Children.Add(element);
    }
    /// <summary>A device on the radar: a pulsing dot and name tag; clicking it opens the device card.</summary>
    private void Blip(int index, int count, string name, string color, string automation, FrameworkElement details)
    {
        // Alternate the inner and middle rings so neighbours never overlap their name tags.
        double radius = index % 2 == 0 ? 235 : 170, angle = (-140 + index * 360.0 / Math.Max(count, 1)) * Math.PI / 180;
        double x = Middle + radius * Math.Cos(angle), y = Middle + radius * Math.Sin(angle);
        Grid dot = new()
        {
            Width = 28,
            Height = 28
        };
        Ellipse halo = new()
        {
            Width = 28,
            Height = 28,
            Opacity = 0.22
        }, core = new()
        {
            Width = 11,
            Height = 11
        };
        halo.SetResourceReference(Shape.FillProperty, color);
        core.SetResourceReference(Shape.FillProperty, color);
        dot.Children.Add(halo);
        dot.Children.Add(core);
        if (!app.Preview && SystemParameters.ClientAreaAnimation)
            halo.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.32, 0.06, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(index * 400) });
        Border tag = new()
        {
            CornerRadius = new(999),
            Padding = new(10, 4, 10, 4),
            BorderThickness = new(1),
            Margin = new(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = C.Text(name, 12, "Text")
        };
        ((TextBlock)tag.Child).TextWrapping = TextWrapping.NoWrap;
        ((TextBlock)tag.Child).MaxWidth = 150;
        ((TextBlock)tag.Child).TextTrimming = TextTrimming.CharacterEllipsis;
        tag.SetResourceReference(Border.BackgroundProperty, "Menu");
        tag.SetResourceReference(Border.BorderBrushProperty, "GlassBorder");
        Button blip = new()
        {
            Style = (Style)app.Window.FindResource("Plain"),
            Content = C.Inline(dot, tag),
            Padding = new(2),
            ToolTip = automation
        };
        AutomationProperties.SetName(blip, automation);
        Popup card = new()
        {
            PlacementTarget = blip,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = details,
            VerticalOffset = 6
        };
        blip.Click += (_, _) => card.IsOpen = !card.IsOpen;
        Place(blip, x - 16, y - 16);
    }
    private static Border Details(string name, string state, bool online, string address, string trust, Button first, Button? second)
    {
        DockPanel top = new()
        {
            Margin = new(0, 0, 0, 8)
        };
        Border chip = C.Badge(state, online ? "Success" : "Muted", online ? "SuccessSoft" : "Glass");
        DockPanel.SetDock(chip, Dock.Right);
        top.Children.Add(chip);
        top.Children.Add(C.Text(name, 13, bold: true));
        StackPanel actions = C.Inline(first);
        if (second != null)
        {
            second.Margin = new(6, 0, 0, 0);
            actions.Children.Add(second);
        }
        Border card = new()
        {
            Width = 250,
            Padding = new(14),
            CornerRadius = new(16),
            BorderThickness = new(1),
            Child = C.Stack(top, C.Text(address, 12, "Muted"), C.Space(C.Text(trust, 12, "Muted"), 12, 2), actions)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassBorder");
        return card;
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
