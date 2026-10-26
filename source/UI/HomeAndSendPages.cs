using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace Lantoss.UI;

internal sealed class HomePage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel devices = new();
    private readonly TextBlock status = C.Text("", 12, "Muted");
    public FrameworkElement View
    {
        get;
    }
    public HomePage(AppShell app)
    {
        this.app = app;
        // Home has one starting action. Detailed history, settings and sync stay in the sidebar.
        Border send = Dropzone(app);
        send.MaxWidth = 920;
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        Button phone = C.Button("Open phone & browser", () => app.Navigate("Phone & browser"), icon: "devices");
        View = C.Stack(C.Space(C.Text("Share something.", 30, bold: true), 8),
            C.Space(C.Text("Pick files, then choose a device on your Wi-Fi.", 14, "Muted"), 24),
            C.Space(send, 26), C.Heading("Your devices", C.Small("Manage devices", () => app.Navigate("Devices"))),
            C.Space(devices, 22), C.Card(Row(C.Stack(C.Space(C.Text("Sharing with your phone?", 15, bold: true), 6),
                C.Text("Open the transfer page or scan its QR code.", 12, "Muted")), phone), 18),
            C.Space(status, 0, 18));
    }
    private static DockPanel Row(FrameworkElement description, FrameworkElement action)
    {
        // These short rows fit beside their action even on a small laptop; wrap only the text.
        DockPanel row = new();
        action.Margin = new(18, 0, 0, 0);
        action.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        description.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(description);
        return row;
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
        devices.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => !p.Blocked && (p.Online || p.Paired)
            && p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Online).ThenBy(p => p.Name).Take(4).ToArray();
        if (peers.Length == 0)
        {
            devices.Children.Add(C.Card(Row(C.Stack(
                C.Space(C.Text(app.Search.Length > 0 ? "No matching devices" : "Open Lantoss on your other PC", 15, bold: true), 6),
                C.Text("Use the same home Wi-Fi. Your PC will appear here automatically.", 12, "Muted")),
                C.Button("Find a device", () => app.Dialogs.Pair(), icon: "devices")), 18));
        }
        foreach (Peer peer in peers)
        {
            Button action = C.Button(peer.Paired ? "Send files" : "Connect", () =>
            {
                if (peer.Paired)
                {
                    app.SelectedPeerKey = peer.Fingerprint;
                    app.Navigate("Send");
                }
                else
                    app.Dialogs.Pair(peer);
            }, icon: peer.Paired ? "send" : "pair");
            devices.Children.Add(C.Space(C.Card(Row(C.Person(peer.Name, null, peer.Online ? "On your network" : "Offline · open Lantoss on this PC", 40, peer.Online ? "Success" : "Muted"), action), 16), 8));
        }
        foreach (BrowserDevice browser in app.Engine.Browser.Devices.Where(b => b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).Take(3))
            devices.Children.Add(C.Space(C.Card(Row(C.Person(browser.Label, browser.Picture, "Connected through the web · " + browser.Address, 40, "Success"), C.Button("Share with browser", () => app.Navigate("Phone & browser"), icon: "devices")), 16), 8));
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        status.Text = speed > 0 ? $"{speed / 1048576:0.0} MB/s transferring" : "";
        status.Visibility = speed > 0 ? Visibility.Visible : Visibility.Collapsed;
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
