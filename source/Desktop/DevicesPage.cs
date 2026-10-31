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
        View = C.Stack(C.Header("Manage", "Devices", "PCs running hopo on your Wi‑Fi appear here automatically.", C.Inline(C.AsyncButton("Diagnostics", app.Dialogs.DiagnosticsAsync), C.AsyncButton("Connect manually", () => app.Dialogs.PairManuallyAsync(), icon: "pair"), C.Button("Find nearby PCs", Scan, true, "sync"))),
            C.Space(C.Text("Discovery uses mDNS and LAN broadcast, and a new device is approved once on both screens. Paired devices reconnect automatically.", 12, "Muted"), 6), C.Space(status, 22), list,
            C.Space(C.Note("Using your phone?", "Open Phone & browser in the menu and scan its QR code. Phones use the browser page, so no app is needed.", "phone"), 0, 20));
    }
    public void Refresh()
    {
        status.Text = app.Engine.DiscoveryStatus;
        list.Children.Clear();
        Peer[] peers = app.Engine.Peers.Where(p => p.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        int paired = peers.Count(p => p.Paired && !p.Blocked);
        list.Children.Add(C.Heading("On your Wi‑Fi", C.Badge(paired + " paired", paired > 0 ? "Mint" : "Muted", paired > 0 ? "Mint/12" : "Text/6")));
        if (peers.Length == 0)
            list.Children.Add(C.Space(C.Empty("Waiting for your other computer…", "Open hopo on the other computer and use the same Wi-Fi. Its name should appear automatically. If it stays missing, use Connection diagnostics or Manual connection."), 14));
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
            string availability = peer.Online ? peer.Paired ? "Ready to send · remembered device" : peer.RequiresCode ? "Ready to connect · access code required" : peer.SupportsDirectConnect ? "Ready to connect · no code needed" : peer.SupportsQuickPair ? "Ready to connect · comparison approval" : "Update both devices for connection without typing, or use manual connection." : "Offline or not yet checked. Open hopo on that computer.";
            StackPanel details = C.Stack(C.Space(C.Person(peer.Name, null, peer.Online ? "Online · " + peer.Address : "Offline", 44, peer.Online ? "Success" : "Muted"), 12),
                C.Space(C.Badge(peer.TrustLabel, peer.Paired ? "Success" : "Muted", peer.Paired ? "SuccessSoft" : "Text/6"), 10), C.Space(C.Text(availability, 12, "Muted"), 14), actions,
                C.Space(C.Text(peer.Endpoint + " · public key " + Format.ShortKey(peer.Fingerprint), 11, "Muted"), 0, 12));
            list.Children.Add(C.Space(C.Card(details), 14));
        }
        BrowserDevice[] browsers = app.Engine.Browser.Devices.Where(b => b.Label.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || b.Address.Contains(app.Search, StringComparison.Ordinal)).ToArray();
        list.Children.Add(C.Space(C.Heading("Through the browser", C.Inline(C.Badge(browsers.Length + " active", browsers.Length > 0 ? "Phone" : "Muted", browsers.Length > 0 ? "Phone/14" : "Text/6"), C.Small("Phone & browser settings", () => app.Navigate("Phone & browser")))), 12, 14));
        if (browsers.Length == 0)
            list.Children.Add(C.Space(C.Text("No phone or browser is connected. Open Phone & browser to share the page link or QR code.", 12, "Muted"), 14));
        foreach (BrowserDevice browser in browsers.Where(b => !b.Approved))
            list.Children.Add(C.Space(Waiting(app, browser), 14));
        foreach (BrowserDevice browser in browsers.Where(b => b.Approved))
        {
            string seen = DateTime.UtcNow - browser.LastSeenUtc < TimeSpan.FromMinutes(1) ? "active now" : "active " + browser.LastSeenUtc.ToLocalTime().ToString("HH:mm");
            Button disconnect = C.Small("Disconnect", async () => { if (await app.Dialogs.ConfirmAsync("Disconnect browser", "Disconnect " + browser.Label + "? Its pending uploads stop and the page must connect again.", "Disconnect")) app.Engine.Browser.Disconnect(browser.Handle); });
            list.Children.Add(C.Space(C.Card(C.Stack(C.Space(C.Person(browser.Label, browser.Picture, browser.Address + " · connected " + browser.ConnectedUtc.ToLocalTime().ToString("HH:mm") + " · " + seen, 44), 12),
                C.Space(C.Text(browser.Name.Length > 0 ? "Name and picture are chosen on that device." : "This browser has not set a name yet.", 11, "Muted"), 12), C.Inline(disconnect, BlockButton(app, browser)))), 14));
        }
        string[] blocked = app.Engine.Browser.BlockedAddresses;
        if (blocked.Length == 0)
            return;
        StackPanel addresses = C.Stack(C.Space(C.Text("Blocked addresses", 13, bold: true), 4), C.Space(C.Text("Browsers from these addresses are refused until hopo restarts.", 11, "Muted"), 10));
        foreach (string address in blocked)
            addresses.Children.Add(C.Space(C.Heading(address, C.Small("Unblock", () => app.Engine.Browser.Unblock(address)), 12), 6));
        list.Children.Add(C.Space(C.Card(addresses, 18), 14));
    }
    /// <summary>A browser waiting for Allow, shared with the Phone page.</summary>
    public static Border Waiting(Shell app, BrowserDevice browser) => C.Card(C.Stack(C.Space(C.Person(browser.Label, browser.Picture, browser.Address + " · asked " + browser.ConnectedUtc.ToLocalTime().ToString("HH:mm"), 44), 10),
        C.Space(C.Badge("Waiting for Allow"), 12), C.Inline(C.Small("Allow", () => app.Engine.Browser.Approve(browser.Handle)), BlockButton(app, browser))));
    private static Button BlockButton(Shell app, BrowserDevice browser) => C.Small("Block", async () => { if (await app.Dialogs.ConfirmAsync("Block browser", "Block " + browser.Label + "? It is disconnected and its address " + browser.Address + " is refused until hopo restarts.", "Block")) app.Engine.Browser.Block(browser.Handle); });
}
