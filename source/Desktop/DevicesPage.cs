using Avalonia.Controls;
using Avalonia.Layout;

namespace Lantoss.Desktop;

internal sealed class DevicesPage : IPage
{
    private readonly Shell app;
    private readonly StackPanel list = new();
    private readonly TextBlock status = C.Text("", 12, "Muted");
    public Control View
    {
        get;
    }
    public DevicesPage(Shell app)
    {
        this.app = app;
        void Scan()
        {
            if (!app.Engine.Running)
                app.ToggleReceiving();
            app.Engine.RefreshDiscovery();
            Refresh();
        }
        View = C.Stack(C.Space(C.Text("Your devices, connected automatically.", 26, bold: true), 9),
            C.Space(C.Text("Open Lantoss on your other computers. Discovery uses mDNS and LAN broadcast, and direct connections are automatic unless code protection is enabled.", 13, "Muted"), 22),
            C.Space(C.Wrap(C.Button("Find nearby devices", Scan, true, "sync"), C.AsyncButton("Manual connection…", () => app.Dialogs.PairManuallyAsync(), icon: "pair"), C.AsyncButton("Connection diagnostics", app.Dialogs.DiagnosticsAsync)), 10),
            C.Space(status, 22), list,
            C.Space(C.Note("Using your phone?", "Open Phone & browser in the sidebar and scan its QR code. Phones use the browser page, so no app is needed.", "phone"), 0, 20));
    }
    public void Refresh()
    {
        status.Text = app.Engine.DiscoveryStatus;
        list.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (peers.Length == 0)
            list.Children.Add(C.Space(C.Empty("Waiting for your other computer…", "Open Lantoss on the other computer and use the same Wi-Fi. Its name should appear automatically. If it stays missing, use Connection diagnostics or Manual connection."), 14));
        foreach (Peer peer in peers)
        {
            StackPanel actions = C.Inline();
            if (!peer.Paired && !peer.Blocked)
                actions.Children.Add(C.Button(peer.SupportsQuickPair || peer.SupportsDirectConnect ? "Connect" : "Use manual connection", () => app.Dialogs.Pair(peer), peer.SupportsQuickPair || peer.SupportsDirectConnect, "pair"));
            if (peer.Paired && !peer.Blocked)
            {
                actions.Children.Add(C.Small("Send", () => { app.SelectedPeerKey = peer.Fingerprint; app.Navigate("Send"); }));
                actions.Children.Add(C.Small("Shared folders", () => app.Navigate("Shared Spaces")));
                actions.Children.Add(C.Small(peer.Favorite ? "Unfavorite" : "Favorite", () => app.Engine.EditPeer(peer with { Favorite = !peer.Favorite })));
                actions.Children.Add(C.Small("Rename", async () =>
                {
                    string? name = await app.Dialogs.PromptAsync("Local device name", "Shown on this computer only", peer.Name);
                    if (name != null)
                        app.Engine.EditPeer(peer with
                        {
                            Name = MetadataStore.ValidateName(name)
                        });
                }));
                actions.Children.Add(C.Small("Unpair", async () => { if (await app.Dialogs.ConfirmAsync("Unpair device", "Unpair " + peer.Name + "? Future transfers will require pairing again. Active transfers will stop.", "Unpair")) app.Engine.Forget(peer, false); }));
                actions.Children.Add(C.Small("Block", async () => { if (await app.Dialogs.ConfirmAsync("Block device", "Block this public key and stop its active transfers?", "Block")) app.Engine.Forget(peer, true); }));
            }
            if (peer.Blocked)
                actions.Children.Add(C.Small("Unblock", () => { app.Store.ForgetPeer(peer.Fingerprint, false); Refresh(); }));
            string availability = peer.Online ? peer.Paired ? "Ready to send · remembered device" : peer.RequiresCode ? "Ready to connect · access code required" : peer.SupportsDirectConnect ? "Ready to connect · no code needed" : peer.SupportsQuickPair ? "Ready to connect · comparison approval" : "Update both devices for connection without typing, or use manual connection." : "Offline or not yet checked. Open Lantoss on that computer.";
            StackPanel details = C.Stack(C.Space(C.Person(peer.Name, null, peer.Online ? "Online · " + peer.Address : "Offline", 44, peer.Online ? "Success" : "Muted"), 12),
                C.Space(C.Badge(peer.TrustLabel, peer.Paired ? "Success" : "Muted", peer.Paired ? "SuccessSoft" : "Background"), 10), C.Space(C.Text(availability, 12, "Muted"), 14), actions,
                C.Space(C.Text(peer.Endpoint + " · public key " + Format.ShortKey(peer.Fingerprint), 11, "Muted"), 0, 12));
            list.Children.Add(C.Space(C.Card(details), 14));
        }
        BrowserDevice[] browsers = app.Engine.Browser.Devices.Where(b => b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || b.Address.Contains(app.Search, StringComparison.Ordinal)).ToArray();
        list.Children.Add(C.Space(C.Heading("Connected through the web", C.Badge(browsers.Length + " active", browsers.Length > 0 ? "Success" : "Muted", browsers.Length > 0 ? "SuccessSoft" : "Background")), 12, 14));
        if (browsers.Length == 0)
            list.Children.Add(C.Space(C.Text("No phone or browser is connected. Open Phone & browser to share the page link or QR code.", 12, "Muted"), 14));
        foreach (BrowserDevice browser in browsers)
        {
            string seen = DateTime.UtcNow - browser.LastSeenUtc < TimeSpan.FromMinutes(1) ? "active now" : "active " + browser.LastSeenUtc.ToLocalTime().ToString("HH:mm");
            Button disconnect = C.Small("Disconnect", async () => { if (await app.Dialogs.ConfirmAsync("Disconnect browser", "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect")) app.Engine.Browser.Disconnect(browser.Handle); });
            disconnect.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(C.Space(C.Card(C.Stack(C.Space(C.Person(browser.Label, browser.Picture, browser.Address + " · connected " + browser.ConnectedUtc.ToLocalTime().ToString("HH:mm") + " · " + seen, 44), 12),
                C.Space(C.Text(browser.Name.Length > 0 ? "Name and picture are chosen on that device." : "This browser has not set a name yet.", 11, "Muted"), 12), disconnect)), 14));
        }
    }
}
