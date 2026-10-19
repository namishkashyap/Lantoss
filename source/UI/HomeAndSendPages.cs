using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace Lantoss.UI;

internal sealed class HomePage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel devices = new(), recent = new(), connection = new();
    private readonly StackPanel deviceFilters = new() { Orientation = Orientation.Horizontal };
    private bool pairedFilter;
    public FrameworkElement View
    {
        get;
    }
    public HomePage(AppShell app)
    {
        this.app = app;
        UniformGrid actions = new()
        {
            Columns = 3,
            Margin = new(-6, 0, -6, 0)
        };
        actions.Children.Add(Quick("Send files", "Choose files or folders", "send", app.PickFiles));
        actions.Children.Add(Quick("Phone & browser", "Scan the QR to share", "devices", () => app.Navigate("Phone & browser")));
        actions.Children.Add(Quick("Connect a PC", "Direct. No code needed.", "pair", () => app.Dialogs.Pair()));
        StackPanel left = C.Stack(C.Heading("Quick actions"), actions);
        View = C.Stack(C.Space(Dropzone(app), 24), C.Heading("Nearby PCs", C.Small("See all →", () => app.Navigate("Devices"))), C.Space(deviceFilters, 12), C.Space(devices, 24), C.Space(C.Columns(left, C.Card(connection), 340), 26), C.Heading("Recent transfers", C.Small("View history →", () => app.Navigate("History"))), recent);
    }
    private static Button Quick(string title, string subtitle, string icon, Action action)
    {
        Button button = C.Button(title, action);
        FrameworkElement image = C.Icon(icon, 25);
        image.HorizontalAlignment = HorizontalAlignment.Left;
        button.Content = C.Stack(C.Space(image, 13), C.Space(C.Text(title, 13, bold: true), 6), C.Text(subtitle, 11, "Muted"));
        button.Padding = new(19, 20, 19, 20);
        button.Margin = new(6, 0, 6, 0);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        return button;
    }
    public static Border Dropzone(AppShell app)
    {
        Grid layout = new()
        {
            MinHeight = 222
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
        TextBlock title = C.Text("Drop files or folders here", 22, bold: true);
        title.TextAlignment = TextAlignment.Center;
        TextBlock subtitle = C.Text("Choose what to share. Keep the rest private.", 12, "Muted");
        subtitle.TextAlignment = TextAlignment.Center;
        Button files = C.Button("Browse files", app.PickFiles, true), folders = C.Button("Browse folders", app.PickFolders);
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
        devices.Children.Clear();
        deviceFilters.Children.Clear();
        Peer[] known = app.Engine.Peers.Where(p => !p.Blocked).ToArray();
        Button nearby = C.Small("Nearby (" + known.Count(p => p.Online) + ")", () => { pairedFilter = false; Refresh(); });
        Button paired = C.Small("Paired (" + known.Count(p => p.Paired) + ")", () => { pairedFilter = true; Refresh(); });
        paired.Margin = new(8, 0, 0, 0);
        nearby.SetResourceReference(Control.BackgroundProperty, pairedFilter ? "Surface" : "PrimarySoft");
        paired.SetResourceReference(Control.BackgroundProperty, pairedFilter ? "PrimarySoft" : "Surface");
        Button team = C.Small("Team · planned", () => { });
        team.IsEnabled = false;
        team.ToolTip = "Team workspaces are planned for a later phase.";
        team.Margin = new(8, 0, 0, 0);
        deviceFilters.Children.Add(nearby);
        deviceFilters.Children.Add(paired);
        deviceFilters.Children.Add(team);
        Peer[] peers = known.Where(p => (pairedFilter ? p.Paired : p.Online) && p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).Take(4).ToArray();
        if (peers.Length == 0)
        {
            StackPanel explanation = C.Stack(C.Space(C.Text(app.Search.Length > 0 ? "No PCs match your search." : "Open Lantoss on your other PC.", 15, bold: true), 7), C.Text(app.Engine.Running ? "Its name will appear here. Click Connect to connect directly. For a phone, use Phone & browser." : "Nearby connection starts when you open the app. If you stopped sharing, click Find nearby PCs to turn it on again.", 12, "Muted"));
            devices.Children.Add(C.Card(C.Columns(explanation, C.Button("Find nearby PCs", () => app.Dialogs.Pair(), icon: "devices"), 185)));
        }
        else
        {
            UniformGrid grid = new()
            {
                Columns = Math.Min(peers.Length, 4),
                Margin = new(-6, 0, -6, 0)
            };
            foreach (Peer peer in peers)
            {
                FrameworkElement graphic = C.Icon("devices", 35);
                graphic.HorizontalAlignment = HorizontalAlignment.Left;
                Border card = C.Card(C.Stack(C.Space(graphic, 15), C.Space(C.Text(peer.Name, 13, bold: true), 7), C.Space(C.Text(peer.TrustLabel, 11, peer.Paired ? "Success" : "Muted"), 13), C.Small(peer.Paired ? "Send files" : peer.SupportsQuickPair ? "Connect" : "Use code", () => { if (peer.Paired) { app.SelectedPeerKey = peer.Fingerprint; app.Navigate("Send"); } else app.Dialogs.Pair(peer); })), 18);
                card.Margin = new(6, 0, 6, 0);
                grid.Children.Add(card);
            }
            devices.Children.Add(grid);
        }
        connection.Children.Clear();
        connection.Children.Add(C.Heading("Connection & security", C.Badge("LAN mode"), 14));
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        connection.Children.Add(C.Space(C.Text(speed > 0 ? $"{speed / 1048576:0.0} MB/s" : "Ready when you are", 21, bold: true), 7));
        connection.Children.Add(C.Space(C.Text(speed > 0 ? "Measured active file throughput" : "Transfer speed appears while files move.", 11, "Muted"), 18));
        connection.Children.Add(C.Space(C.Text(app.Engine.Running ? "Receiving · " + app.Engine.Endpoint : "Receiving is off", 11, "Muted"), 11));
        connection.Children.Add(C.Text("Native: TLS · browser: local HTTP", 11, "Success", true));
        recent.Children.Clear();
        TransferRecord[] records = app.Engine.Transfers.Records.Take(4).ToArray();
        if (records.Length == 0)
            recent.Children.Add(C.Card(C.Text("Your transfers will appear here. Nothing has been sent or received yet.", 12, "Muted")));
        else
        {
            foreach (TransferRecord record in records)
                recent.Children.Add(C.Space(HistoryPage.Summary(record), 9));
        }
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
        View = C.Stack(C.Space(C.Text("Choose what moves. Choose where it goes.", 24, bold: true), 9), C.Space(C.Text("Choose one paired recipient for each job. Both apps must stay open.", 13, "Muted"), 24), C.Columns(C.Card(left), right, 325));
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
}
