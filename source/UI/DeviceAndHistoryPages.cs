using System.Text;
using Microsoft.Win32;

namespace Lantoss.UI;

internal sealed class DevicesPage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel list = new();
    private readonly TextBlock status = C.Text("", 12, "Muted");
    public FrameworkElement View
    {
        get;
    }
    public DevicesPage(AppShell app)
    {
        this.app = app;
        Button scan = C.Button("Find nearby PCs", () => { if (!app.Engine.Running) app.ToggleReceiving(); app.Engine.RefreshDiscovery(); Refresh(); }, true, "sync");
        Button pair = C.Button("Manual connection…", () => app.Dialogs.PairManually(), icon: "pair");
        pair.Margin = new(10, 0, 0, 0);
        Button diagnostics = C.Button("Connection diagnostics", () => NetworkDiagnostics.Show(app));
        diagnostics.Margin = new(10, 0, 0, 0);
        View = C.Stack(C.Hero("Manage", "Devices", "PCs running hopo on your Wi‑Fi appear here automatically. A new PC is approved once on both screens, then reconnects by itself."), C.Space(C.Inline(scan, pair, diagnostics), 18), C.Space(status, 22), list,
            C.Space(C.Note("Using your phone?", "Open Phone & browser from the menu and scan its QR code. Phones use the browser page; NAS agents and internet sharing remain unavailable.", "devices"), 0, 20));
    }
    public void Refresh()
    {
        status.Text = app.Engine.DiscoveryStatus;
        list.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (peers.Length == 0)
            list.Children.Add(C.Empty("Waiting for your other PC…", "Open this version of hopo on the other PC and use the same Wi-Fi. Its name should appear automatically. If it stays missing, check Connection help in Settings or use Manual connection.", action: C.Button("Find nearby PCs", () => { if (!app.Engine.Running) app.ToggleReceiving(); app.Engine.RefreshDiscovery(); }, icon: "sync")));
        foreach (Peer peer in peers)
        {
            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal
            };
            if (!peer.Paired && !peer.Blocked)
                actions.Children.Add(C.Button(peer.SupportsQuickPair ? "Connect" : "Use manual connection", () => app.Dialogs.Pair(peer), peer.SupportsQuickPair, "pair"));
            if (peer.Paired && !peer.Blocked)
            {
                actions.Children.Add(C.Small("Send", () => { app.SelectedPeerKey = peer.Fingerprint; app.Navigate("Send"); }));
                Button browse = C.Small("Shared folders", () => app.Navigate("Shared Spaces"));
                browse.Margin = new(7, 0, 0, 0);
                actions.Children.Add(browse);
                Button favorite = C.Small(peer.Favorite ? "Unfavorite" : "Favorite", () => app.Engine.EditPeer(peer with { Favorite = !peer.Favorite }));
                favorite.Margin = new(7, 0, 0, 0);
                actions.Children.Add(favorite);
                Button rename = C.Small("Rename", () => Rename(peer));
                rename.Margin = new(7, 0, 0, 0);
                actions.Children.Add(rename);
                Button forget = C.Small("Unpair", () => { if (MessageBox.Show(app.Window, "Unpair " + peer.Name + "? Future transfers will require pairing again. Active transfers will stop.", "Unpair device", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Forget(peer, false); });
                forget.Margin = new(7, 0, 0, 0);
                actions.Children.Add(forget);
                Button block = C.Small("Block", () => { if (MessageBox.Show(app.Window, "Block this public key and stop its active transfers?", "Block device", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Forget(peer, true); });
                block.Margin = new(7, 0, 0, 0);
                actions.Children.Add(block);
            }
            if (peer.Blocked)
                actions.Children.Add(C.Small("Unblock", () => { app.Store.ForgetPeer(peer.Fingerprint, false); Refresh(); }));
            DockPanel name = C.Person(peer.Name, null, peer.Online ? "Online · " + peer.Address : "Offline", 44, peer.Online ? "Success" : "Muted");
            Expander connectionDetails = new()
            {
                Header = "Connection details",
                Content = C.Stack(C.Space(C.Text(peer.Endpoint, 11, "Muted"), 7), C.Text("Public key: " + Format.ShortKey(peer.Fingerprint), 11, "Muted")),
                Margin = new(0, 15, 0, 0)
            };
            connectionDetails.SetResourceReference(Control.ForegroundProperty, "Muted");
            string availability = peer.Online ? peer.Paired ? "Ready to send · remembered PC" : peer.RequiresCode ? "Ready to connect · access code required" : peer.SupportsDirectConnect ? "Ready to connect · no code needed" : peer.SupportsQuickPair ? "Ready to connect · comparison approval" : "Update both PCs for connection without typing, or use manual connection." : "Offline or not yet checked. Open hopo on that PC.";
            Border trust = C.Badge(peer.TrustLabel, peer.Paired ? "Success" : "Muted", peer.Paired ? "SuccessSoft" : "Background");
            trust.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel details = C.Stack(C.Space(name, 12), C.Space(trust, 10), C.Space(C.Text(availability, 12, "Muted"), 16), actions, connectionDetails);
            list.Children.Add(C.Space(C.Card(details), 14));
        }
        BrowserDevice[] browsers = app.Engine.Browser.Devices.Where(b => b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || b.Address.Contains(app.Search, StringComparison.Ordinal)).ToArray();
        list.Children.Add(C.Space(C.Heading("Connected through the web", C.Badge(browsers.Length + " active", browsers.Length > 0 ? "Success" : "Muted", browsers.Length > 0 ? "SuccessSoft" : "Background")), 12, 14));
        if (browsers.Length == 0)
            list.Children.Add(C.Space(C.Text("No phone or browser is connected. Open Phone & browser to share the page link or QR code.", 12, "Muted"), 14));
        foreach (BrowserDevice browser in browsers)
        {
            Button disconnect = C.Small("Disconnect", () => { if (MessageBox.Show(app.Window, "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect browser", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Browser.Disconnect(browser.Handle); });
            string seen = DateTime.UtcNow - browser.LastSeenUtc < TimeSpan.FromMinutes(1) ? "active now" : "active " + browser.LastSeenUtc.ToLocalTime().ToString("HH:mm");
            Button block = C.Small("Block", () => { if (MessageBox.Show(app.Window, "Block " + browser.Address + "? " + browser.Label + " is disconnected and this address cannot open the page until hopo restarts.", "Block browser", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes) app.Engine.Browser.Block(browser.Handle); });
            block.Margin = new(8, 0, 0, 0);
            StackPanel actions = browser.Approved ? C.Inline(disconnect, block) : C.Inline(C.Small("Allow", () => app.Engine.Browser.Approve(browser.Handle)), block);
            Border state = C.Badge(browser.Approved ? "Allowed" : "Waiting for Allow", browser.Approved ? "Success" : "Danger", browser.Approved ? "SuccessSoft" : "DangerSoft");
            state.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel details = C.Stack(C.Space(C.Person(browser.Label, browser.Picture, browser.Address + " · connected " + browser.ConnectedUtc.ToLocalTime().ToString("HH:mm") + " · " + seen, 44), 12), C.Space(state, 10),
                C.Space(C.Text(browser.Approved ? browser.Name.Length > 0 ? "Name and picture are chosen on that device." : "This browser has not set a name yet." : "Allow only a phone you recognise. Until then it cannot see files, folders or notes.", 11, "Muted"), 12), actions);
            list.Children.Add(C.Space(C.Card(details), 14));
        }
        foreach (string address in app.Engine.Browser.BlockedAddresses)
            list.Children.Add(C.Space(C.Card(C.Stack(C.Space(C.Text("Blocked address " + address, 13, bold: true), 10), C.Small("Unblock", () => { app.Engine.Browser.Unblock(address); Refresh(); }))), 14));
    }
    private void Rename(Peer peer)
    {
        Window dialog = new()
        {
            Owner = app.Window,
            Title = "Local device name",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Resources.MergedDictionaries.Add(app.Window.Resources);
        dialog.SetResourceReference(Control.BackgroundProperty, "Background");
        TextBox name = new()
        {
            Text = peer.Name,
            MaxLength = 64,
            Margin = new(0, 10, 0, 18)
        };
        Button save = C.Button("Save local name", () => { app.Engine.EditPeer(peer with { Name = MetadataStore.ValidateName(name.Text) }); dialog.Close(); }, true);
        StackPanel body = C.Stack(C.Text("Shown on this PC only", 17, bold: true), name, save);
        body.Margin = new(25);
        dialog.Content = body;
        dialog.ShowDialog();
    }
}

internal sealed class HistoryPage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel list = new();
    public FrameworkElement View
    {
        get;
    }
    public HistoryPage(AppShell app)
    {
        this.app = app;
        Button export = C.Button("Export log", Export), clear = C.Button("Clear finished history", Clear);
        clear.Margin = new(9, 0, 0, 0);
        View = C.Stack(C.Hero("Manage", "History", "What moved, and when. File contents are never stored."), C.Space(C.Inline(export, clear), 22), list);
    }
    public void Refresh()
    {
        list.Children.Clear();
        TransferRecord[] records = app.Engine.Transfers.Records.Where(r => r.Terminal && (r.Title.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.PeerName.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.Files.Any(f => f.RelativePath.Contains(app.Search, StringComparison.OrdinalIgnoreCase)))).Take(100).ToArray();
        if (records.Length == 0)
            list.Children.Add(C.Empty("No matching history.", "Completed, declined, cancelled, and failed transfers appear here. Clearing history keeps received files.", "history"));
        foreach (TransferRecord record in records)
        {
            StackPanel container = C.Stack(Summary(record));
            if (record.Direction == "Received" && Directory.Exists(record.Destination))
                container.Children.Add(C.Space(C.Small("Reveal receive folder", () => app.Reveal(record.Destination)), 0, 8));
            list.Children.Add(C.Space(container, 14));
        }
    }
    public static Border Summary(TransferRecord record)
    {
        Border badge = C.Badge(record.StatusText, record.State == TransferState.Completed ? "Success" : record.State == TransferState.Failed ? "Danger" : "Primary", record.State == TransferState.Completed ? "SuccessSoft" : record.State == TransferState.Failed ? "DangerSoft" : "PrimarySoft");
        StackPanel info = C.Stack(C.Heading(record.Title, badge, 13), C.Text($"{(record.Direction == "Received" ? "From" : "To")} {record.PeerName}{(record.PeerAddress.Length > 0 ? " · " + record.PeerAddress : "")} · {record.SizeText} · {record.CreatedUtc.ToLocalTime():dd MMM, HH:mm}", 11, "Muted"));
        return C.Card(info, 17);
    }
    private void Clear()
    {
        if (MessageBox.Show(app.Window, "Clear finished transfer metadata? Received files will stay on disk. Active transfers will remain listed.", "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        app.Engine.Transfers.ClearFinished();
        Refresh();
        app.Tell("Finished history cleared. Received files were kept.");
    }
    private void Export()
    {
        SaveFileDialog picker = new()
        {
            Title = "Export transfer metadata",
            FileName = "hopo-history.csv",
            Filter = "CSV files (*.csv)|*.csv"
        };
        if (picker.ShowDialog(app.Window) != true)
            return;
        StringBuilder csv = new("Time UTC,Direction,PC,Status,Files,Bytes,Verified files,Skipped files\r\n");
        foreach (TransferRecord r in app.Engine.Transfers.Records)
            csv.AppendLine(string.Join(',', new[] { r.CreatedUtc.ToString("O"), r.Direction, r.PeerName, r.State.ToString(), r.FileCount.ToString(), r.TotalBytes.ToString(), r.VerifiedFiles.ToString(), r.SkippedFiles.ToString() }.Select(Cell)));
        File.WriteAllText(picker.FileName, csv.ToString(), new UTF8Encoding(true));
        app.Tell("Exported metadata without file contents, paths, addresses, pairing codes, or keys.");
        static string Cell(string value)
        {
            if (value.Length > 0 && "=+-@".Contains(value[0]))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
