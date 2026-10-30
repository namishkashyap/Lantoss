using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lantoss.Desktop;

internal sealed class HomePage : IPage
{
    private readonly Shell app;
    private readonly StackPanel devices = new();
    private readonly TextBlock status = C.Text("", 12, "Primary", true);
    public Control View
    {
        get;
    }
    public HomePage(Shell app)
    {
        this.app = app;
        Control icon = C.Icon("upload", 40);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock title = C.Text("Drop files here", 22, bold: true), subtitle = C.Text("Files and folders stay on your home network.", 12, "Muted");
        title.TextAlignment = subtitle.TextAlignment = TextAlignment.Center;
        StackPanel buttons = C.Inline(C.AsyncButton("Choose files", app.PickFilesAsync, true, "plus"), C.AsyncButton("Choose folder", app.PickFoldersAsync, icon: "folder"));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        // The whole window accepts dropped files; this area shows where to drop them.
        Border drop = new()
        {
            Child = C.Stack(C.Space(icon, 12), C.Space(title, 7), C.Space(subtitle, 18), buttons),
            Padding = new(24, 40),
            CornerRadius = new(16),
            Background = Theme.Brush("PrimarySoft"),
            BorderBrush = Theme.Brush("Primary"),
            BorderThickness = new(1.2),
            MaxWidth = 920
        };
        View = C.Stack(C.Space(C.Text("Share something.", 32, bold: true), 8), C.Space(C.Text("Pick files, then choose a device on your Wi-Fi.", 14, "Muted"), 24),
            C.Space(drop, 26), C.Heading("Your devices", C.Small("Manage devices", () => app.Navigate("Devices"))), C.Space(devices, 22),
            C.Card(Row(C.Stack(C.Space(C.Text("Sharing with your phone?", 15, bold: true), 6), C.Text("Open the transfer page or scan its QR code.", 12, "Muted")), C.Button("Open phone & browser", () => app.Navigate("Phone & browser"), icon: "phone")), 18),
            C.Space(status, 0, 18));
    }
    public static DockPanel Row(Control description, Control action)
    {
        DockPanel row = new();
        action.Margin = new(18, 0, 0, 0);
        action.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        description.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(description);
        return row;
    }
    public void Refresh()
    {
        devices.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => !p.Blocked && (p.Online || p.Paired) && p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Online).ThenBy(p => p.Name).Take(4).ToArray();
        if (peers.Length == 0)
            devices.Children.Add(C.Card(Row(C.Stack(C.Space(C.Text(app.Search.Length > 0 ? "No matching devices" : "Open Lantoss on your other computer", 15, bold: true), 6),
                C.Text("Use the same home Wi-Fi. It will appear here automatically.", 12, "Muted")), C.Button("Find a device", () => app.Dialogs.Pair(), icon: "devices")), 18));
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
            devices.Children.Add(C.Space(C.Card(Row(C.Person(peer.Name, null, peer.Online ? "On your network" : "Offline · open Lantoss on that computer", 40, peer.Online ? "Success" : "Muted"), action), 16), 8));
        }
        foreach (BrowserDevice browser in app.Engine.Browser.Devices.Where(b => b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).Take(3))
            devices.Children.Add(C.Space(C.Card(Row(C.Person(browser.Label, browser.Picture, "Connected through the web · " + browser.Address, 40, "Success"), C.Button("Share with browser", () => app.Navigate("Phone & browser"), icon: "phone")), 16), 8));
        double speed = app.Engine.Transfers.Records.Where(r => !r.Terminal).Sum(r => r.BytesPerSecond);
        status.Text = speed > 0 ? $"{speed / 1048576:0.0} MB/s transferring" : "";
        status.IsVisible = speed > 0;
    }
}

internal sealed class SendPage : IPage
{
    private sealed record Recipient(string Label, Peer? Peer, BrowserDevice? Browser)
    {
        public override string ToString() => Label;
    }
    private readonly Shell app;
    private string? browserHandle;
    private readonly StackPanel files = new(), recipient = new();
    private readonly TextBlock totals = C.Text("", 12, "Muted");
    public Control View
    {
        get;
    }
    public SendPage(Shell app)
    {
        this.app = app;
        StackPanel left = C.Stack(C.Space(C.Heading("Selected files & folders", C.Small("Clear selection", () => { app.Selection.Clear(); Refresh(); })), 6),
            C.Space(C.Inline(C.AsyncButton("Add files", app.PickFilesAsync, icon: "plus"), C.AsyncButton("Add folders", app.PickFoldersAsync, icon: "folder")), 18), C.Space(totals, 16), files);
        Button phone = C.AsyncButton("Share selection with phone/browser", async () =>
        {
            if (app.Selection.Count > 0)
                await Task.Run(() => app.Engine.Browser.ShareAsync(app.Selection.Select(item => item.Path).ToArray()));
            app.Navigate("Phone & browser");
        }, icon: "phone");
        StackPanel right = C.Stack(C.Card(recipient), C.Space(phone, 0, 16),
            C.Space(C.Note("The recipient stays in control", "They see your name, file count, size, and destination, then choose Accept or Decline."), 0, 18),
            C.Space(C.Note("Native transfers: original bytes, verified", "Folders keep their relative paths. Every file is checked before it is finalized on the other device.", "check"), 0, 18));
        View = C.Stack(C.Space(C.Text("Choose what moves. Choose where it goes.", 26, bold: true), 9), C.Space(C.Text("Choose one paired recipient for each job. Both apps must stay open.", 13, "Muted"), 24), C.Columns(C.Card(left), right, 330));
    }
    public void Refresh()
    {
        files.Children.Clear();
        totals.Text = $"{app.Selection.Count} selections · {app.Selection.Sum(s => s.Files)} files · {Format.Bytes(app.Selection.Sum(s => s.Size))}";
        SelectionItem[] visible = app.Selection.Where(s => s.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visible.Length == 0)
            files.Children.Add(C.Space(C.Text(app.Selection.Count == 0 ? "Add files or folders, or drop them anywhere in this window." : "No selection matches your search.", 13, "Muted"), 30, 20));
        foreach (SelectionItem item in visible)
        {
            Control icon = C.Icon(item.Folders > 0 ? "folder" : "file", 27);
            icon.Margin = new(0, 0, 14, 0);
            Button remove = C.Small("Remove", () => { app.Selection.Remove(item); Refresh(); });
            DockPanel row = new()
            {
                Margin = new(0, 0, 0, 14)
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(C.Stack(C.Space(C.Text(item.Name, 13, bold: true), 5), C.Text(item.Detail, 11, "Muted")));
            files.Children.Add(row);
        }
        recipient.Children.Clear();
        recipient.Children.Add(C.Heading("Send to"));
        Peer[] paired = app.Engine.Peers.Where(p => p.Paired && !p.Blocked).ToArray();
        Recipient[] targets = paired.Select(p => new Recipient(p.Name, p, null)).Concat(app.Engine.Browser.Running ? app.Engine.Browser.Devices.Where(d => d.Approved).Select(d => new Recipient(d.Label + " · Phone / browser", null, d)) : []).ToArray();
        if (targets.Length == 0)
        {
            recipient.Children.Add(C.Space(C.Text("Connect another computer once, then it will be remembered for future transfers.", 12, "Muted"), 18));
            recipient.Children.Add(C.Button("Connect a device", () => app.Dialogs.Pair(), true, "pair"));
            return;
        }
        ComboBox picker = new()
        {
            ItemsSource = targets,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(0, 0, 0, 14),
            SelectedItem = targets.FirstOrDefault(t => t.Browser?.Handle == browserHandle && browserHandle != null) ?? targets.FirstOrDefault(t => t.Peer?.Fingerprint == app.SelectedPeerKey) ?? targets[0]
        };
        TextBlock hint = C.Text("", 11, "Muted");
        void Describe()
        {
            Recipient? chosen = picker.SelectedItem as Recipient;
            hint.Text = chosen?.Browser != null ? "Files are listed on that phone's page only, under Files from your PC. Other phones do not see them." : "The public key is checked again when connecting. Offline devices may need receiving switched on.";
        }
        picker.SelectionChanged += (_, _) =>
        {
            Recipient? chosen = picker.SelectedItem as Recipient;
            browserHandle = chosen?.Browser?.Handle;
            if (chosen?.Peer != null)
                app.SelectedPeerKey = chosen.Peer.Fingerprint;
            Describe();
        };
        Describe();
        recipient.Children.Add(picker);
        recipient.Children.Add(C.Space(hint, 20));
        Button send = C.AsyncButton("Send securely", async () =>
        {
            if (picker.SelectedItem is not Recipient chosen)
                return;
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
        }, true, "send");
        send.IsEnabled = app.Selection.Count > 0;
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        recipient.Children.Add(send);
        recipient.Children.Add(C.Space(C.Text("Duplicate handling is chosen by the recipient. Existing files are never silently overwritten.", 11, "Muted"), 0, 18));
    }
}
