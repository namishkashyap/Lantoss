using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lantoss.Desktop;

/// <summary>Approval and connection dialogs. Engine callbacks arrive off-thread and are serialized so only one approval is shown at a time.</summary>
internal sealed class Dialogs(Shell app)
{
    private readonly SemaphoreSlim approvals = new(1, 1);
    private Window? pairingWindow;
    private Window Create(string title, double width = 580) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        MaxHeight = 860,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = Theme.Brush("Background"),
        Icon = app.Window.Icon
    };
    private static ScrollViewer Body(StackPanel body)
    {
        body.Margin = new(28);
        return new ScrollViewer { Content = body };
    }
    private async Task<T> ShowAsync<T>(Window dialog)
    {
        // Approvals can arrive while the window is minimized; bring hopo forward so the request is seen.
        if (!app.Window.IsVisible || app.Window.WindowState == WindowState.Minimized)
            app.ShowWindow();
        return await dialog.ShowDialog<T>(pairingWindow is { IsVisible: true } ? pairingWindow : app.Window);
    }
    public Task MessageAsync(string title, string text)
    {
        Window window = Create(title, 480);
        window.Content = Body(C.Stack(C.Space(C.Text(title, 20, bold: true), 12), C.Space(C.Text(text, 13), 20), C.Button("OK", () => window.Close(true), true)));
        return ShowAsync<bool>(window);
    }
    public async Task<bool> ConfirmAsync(string title, string text, string confirm = "Yes")
    {
        Window window = Create(title, 500);
        window.Content = Body(C.Stack(C.Space(C.Text(title, 20, bold: true), 12), C.Space(C.Text(text, 13), 22), C.Inline(C.Button("Cancel", () => window.Close(false)), C.Button(confirm, () => window.Close(true), true))));
        return await ShowAsync<bool?>(window) == true;
    }
    public async Task<string?> PromptAsync(string title, string label, string value, int maxLength = 64)
    {
        Window window = Create(title, 460);
        TextBox input = new()
        {
            Text = value,
            MaxLength = maxLength,
            Margin = new(0, 10, 0, 18)
        };
        window.Content = Body(C.Stack(C.Text(label, 17, bold: true), input, C.Inline(C.Button("Cancel", () => window.Close(null)), C.Button("Save", () => window.Close(input.Text ?? ""), true))));
        return await ShowAsync<string?>(window);
    }
    /// <summary>Returns the chosen device keys, an empty array for every paired PC, or null when cancelled.</summary>
    public async Task<string[]?> ChoosePeersAsync(string title, string[]? current)
    {
        Window window = Create(title, 480);
        Peer[] peers = app.Store.Peers.Where(p => p.Paired && !p.Blocked).ToArray();
        CheckBox all = new()
        {
            Content = "All paired PCs",
            IsChecked = current is not { Length: > 0 },
            Margin = new(0, 3, 0, 10)
        };
        CheckBox[] boxes = peers.Select(p => new CheckBox { Content = p.Name + " · " + Format.ShortKey(p.Fingerprint), IsChecked = current?.Contains(p.Fingerprint) == true, Tag = p.Fingerprint, Margin = new(18, 3, 0, 3) }).ToArray();
        Button save = C.Button("Save", () => window.Close(all.IsChecked == true ? Array.Empty<string>() : boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToArray()), true);
        void Update()
        {
            foreach (CheckBox box in boxes)
                box.IsEnabled = all.IsChecked != true;
            save.IsEnabled = all.IsChecked == true || boxes.Any(b => b.IsChecked == true);
        }
        all.IsCheckedChanged += (_, _) => Update();
        foreach (CheckBox box in boxes)
            box.IsCheckedChanged += (_, _) => Update();
        Update();
        StackPanel list = C.Stack(boxes);
        window.Content = Body(C.Stack(C.Space(C.Text(title, 20, bold: true), 8), C.Space(C.Text("Only the chosen paired PCs can open this space. Phones and browsers are not affected.", 12, "Muted"), 14), all,
            C.Space<Control>(peers.Length == 0 ? C.Text("No paired PCs yet. Pair a computer in Devices first.", 12, "Muted") : list, 20), C.Inline(C.Button("Cancel", () => window.Close(null)), save)));
        return await ShowAsync<string[]?>(window);
    }
    public void Pair(Peer? candidate = null)
    {
        if (pairingWindow is { IsVisible: true })
        {
            pairingWindow.Activate();
            return;
        }
        if (!app.Engine.Running)
            app.ToggleReceiving();
        if (candidate == null)
        {
            app.Engine.RefreshDiscovery();
            app.Navigate("Devices");
            app.Tell("Choose the other device below and click Connect. hopo connects directly unless code protection is enabled.");
        }
        else if (candidate.SupportsDirectConnect || candidate.SupportsQuickPair)
            _ = ConnectNearbyAsync(candidate);
        else
            _ = PairManuallyAsync(candidate);
    }
    private async Task ConnectNearbyAsync(Peer peer)
    {
        Window window = Create("Connect to " + peer.Name);
        pairingWindow = window;
        using CancellationTokenSource work = new();
        TextBlock feedback = C.Text(peer.RequiresCode ? "Enter its current code from Phone & browser, then click Connect." : "Connecting directly to the other device…", 13, "Muted");
        TextBox accessCode = new()
        {
            MaxLength = 24,
            FontSize = 20,
            Margin = new(0, 0, 0, 18),
            IsVisible = peer.RequiresCode
        };
        Button connect = C.Button("Connect", () => { }, true, "pair");
        connect.IsVisible = peer.RequiresCode;
        async Task ConnectAsync()
        {
            connect.IsVisible = false;
            feedback.Text = "Connecting…";
            try
            {
                Peer result = peer.SupportsDirectConnect ? await app.Engine.ConnectDirectAsync(peer, accessCode.Text ?? "", work.Token, CompareAsync) : await app.Engine.PairNearbyAsync(peer, CompareAsync, work.Token);
                window.Close();
                app.Navigate("Devices");
                app.Tell("Connected to " + result.Name + ". Remembered for next time; choose Send when you are ready.");
            }
            catch (Exception error)
            {
                if (!work.IsCancellationRequested)
                {
                    feedback.Text = TransferEngine.HumanError(error);
                    if (error is CodeRequiredException)
                        accessCode.IsVisible = true;
                    connect.IsVisible = true;
                }
            }
        }
        connect.Click += async (_, _) => await ConnectAsync();
        window.Content = Body(C.Stack(C.Space(C.Text("Connecting to " + peer.Name, 23, bold: true), 13),
            C.Space(C.Text(peer.RequiresCode ? "Access-code protection is enabled on that device. Both screens will show a code to compare." : "Direct connection. No password or comparison code.", 14, "Primary", true), 17),
            C.Space(feedback, 18), accessCode, C.Space(C.Note("Files still need approval", "Connecting remembers this device's public key. The receiver reviews incoming files before they are saved."), 22),
            C.Inline(C.Button("Cancel", () => window.Close()), connect)));
        window.Opened += async (_, _) => { if (!peer.RequiresCode) await ConnectAsync(); };
        window.Closed += (_, _) => { work.Cancel(); pairingWindow = null; };
        await ShowAsync<object?>(window);
    }
    public async Task PairManuallyAsync(Peer? candidate = null)
    {
        if (pairingWindow is { IsVisible: true })
        {
            pairingWindow.Activate();
            return;
        }
        Window window = Create("Manual connection — fallback", 620);
        pairingWindow = window;
        using CancellationTokenSource work = new();
        TextBlock localAddress = C.Text(app.Engine.Endpoint, 15, bold: true), localCode = C.Text(app.Engine.PairingCode.Value, 25, "Primary", true), expiry = C.Text("", 11, "Muted");
        localCode.FontFamily = new FontFamily("monospace");
        TextBox address = new()
        {
            Text = candidate?.Endpoint ?? "",
            Margin = new(0, 7, 0, 15)
        };
        TextBox code = new()
        {
            FontSize = 20,
            MaxLength = 24,
            Margin = new(0, 7, 0, 15)
        };
        TextBlock feedback = C.Text("Both devices need hopo. Browsers use the Phone & browser page instead.", 12, "Muted");
        Button pair = C.AsyncButton("Connect and compare codes", async () =>
        {
            feedback.Text = "Connecting securely. Keep hopo open on the other device…";
            try
            {
                Peer result = await app.Engine.PairAsync(address.Text ?? "", code.Text ?? "", CompareAsync, work.Token);
                app.Tell("Paired with " + result.Name + ". Every incoming transfer will still ask for approval.");
                window.Close();
                app.Navigate("Devices");
            }
            catch (Exception error) { feedback.Text = TransferEngine.HumanError(error); }
        }, true, "pair");
        pair.HorizontalAlignment = HorizontalAlignment.Stretch;
        Button copy = C.Small("Copy my code", () => _ = app.CopyAsync(app.Engine.PairingCode.Value, "Pairing code copied. It expires after five minutes and one use."));
        Button rotate = C.Small("New code", app.Engine.PairingCode.Rotate);
        StackPanel local = C.Stack(C.Heading("This " + Shell.Platform), C.Space(localAddress, 9), C.Space(localCode, 6), C.Space(expiry, 14), C.Inline(copy, rotate));
        window.Content = Body(C.Stack(C.Space(C.Text("Connect when discovery is unavailable.", 21, bold: true), 10),
            C.Space(C.Text("Normally, choose the other device in Devices and click Connect. Use these fields only if it does not appear, or it runs an older version.", 12, "Muted"), 20),
            C.Space(C.Card(local), 22), C.Text("OTHER DEVICE ADDRESS", 10, "Muted", true), address, C.Text("OTHER DEVICE PAIRING CODE", 10, "Muted", true), code, C.Space(pair, 15), feedback));
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => { localAddress.Text = app.Engine.Endpoint; localCode.Text = app.Engine.PairingCode.Value; expiry.Text = "Valid until " + app.Engine.PairingCode.ExpiresUtc.ToLocalTime().ToString("HH:mm") + " · single use"; };
        timer.Start();
        window.Closed += (_, _) => { timer.Stop(); work.Cancel(); pairingWindow = null; };
        await ShowAsync<object?>(window);
    }
    public async Task<bool> CompareAsync(PairRequest request, CancellationToken token)
    {
        await approvals.WaitAsync(token);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                Window window = Create("Verify this connection");
                TextBlock comparison = C.Text(request.ComparisonCode, 30, "Primary", true);
                comparison.FontFamily = new FontFamily("monospace");
                comparison.TextAlignment = TextAlignment.Center;
                comparison.Margin = new(0, 22);
                window.Content = Body(C.Stack(C.Space(C.Text("Connect to " + request.Name + "?", 23, bold: true), 12),
                    C.Text("Compare this code with the other device's screen. Approve only if you expected this connection and the codes match exactly.", 13, "Muted"), comparison,
                    C.Space(C.Note("Both devices must approve", "This step verifies the public keys, rather than trusting a device name or IP address."), 20),
                    C.Space(C.Text("Peer public key: " + Format.ShortKey(request.Fingerprint), 11, "Muted"), app.Store.NameInUse(request.Name, request.Fingerprint) ? 8 : 22),
                    app.Store.NameInUse(request.Name, request.Fingerprint) ? C.Space(C.Text("Another saved device uses this name. Check the key.", 12, "Danger", true), 22) : new Control(),
                    C.Inline(C.Button("Decline", () => window.Close(false)), C.Button("Codes match — approve", () => window.Close(true), true))));
                using CancellationTokenRegistration cancel = token.Register(() => Dispatcher.UIThread.Post(() => window.Close(false)));
                return await ShowAsync<bool?>(window) == true && !token.IsCancellationRequested;
            });
        }
        finally { approvals.Release(); }
    }
    public async Task<ReceiveDecision> ReceiveAsync(ReceiveRequest request, CancellationToken token)
    {
        await approvals.WaitAsync(token);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                Window window = Create("Incoming file transfer", 660);
                string folder = request.DefaultFolder;
                TextBlock location = C.Text(folder, 11, "Muted"), free = C.Text("", 12, "Muted");
                ComboBox duplicates = new()
                {
                    ItemsSource = new[] { "Keep both (number duplicate names)", "Skip existing files", "Replace existing files (confirm)" },
                    SelectedIndex = 0,
                    IsEnabled = !request.Browser && !request.FixedFolder,
                    Margin = new(0, 8, 0, 18),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Button accept = C.AsyncButton("Accept files", async () =>
                {
                    if (duplicates.SelectedIndex == 2 && !await ConfirmAsync("Confirm replacements", "Replace existing files at matching paths? Only incoming files that pass integrity verification will replace originals.", "Replace"))
                        return;
                    window.Close(true);
                }, true, "download");
                void CheckSpace()
                {
                    try
                    {
                        long available = FileCatalog.FreeSpace(folder);
                        free.Text = Format.Bytes(available) + " available · " + Format.Bytes(request.Manifest.TotalBytes) + " requested";
                        accept.IsEnabled = available >= request.Manifest.TotalBytes + 64L * 1024 * 1024;
                    }
                    catch (IOException) { free.Text = "Destination drive is unavailable. Choose another folder."; accept.IsEnabled = false; }
                }
                Button choose = C.AsyncButton("Change folder", async () =>
                {
                    string[] picked = await app.PickFoldersAsync("Choose the receive folder");
                    if (picked.Length == 0)
                        return;
                    FileCatalog.CheckSafeDirectory(picked[0]);
                    folder = picked[0];
                    location.Text = folder;
                    CheckSpace();
                });
                choose.IsEnabled = !request.FixedFolder;
                StackPanel names = C.Stack();
                foreach (ManifestEntry entry in request.Manifest.Entries.Take(8))
                    names.Children.Add(C.Space(C.Text((entry.Directory ? "Folder · " : "File · ") + entry.RelativePath, 12), 7));
                if (request.Manifest.Entries.Count > 8)
                    names.Children.Add(C.Text("+ " + (request.Manifest.Entries.Count - 8) + " more items", 11, "Muted"));
                string sender = (request.Browser ? "Web browser" : "Computer") + (request.Address.Length > 0 ? " · " + request.Address : "") + " · asked at " + DateTime.Now.ToString("HH:mm");
                StackPanel warnings = C.Stack();
                if (!request.Browser)
                    warnings.Children.Add(C.Space(C.Text("Key " + Format.ShortKey(request.Fingerprint), 11, "Muted"), 8));
                if (!request.Browser && app.Store.NameInUse(request.PeerName, request.Fingerprint))
                    warnings.Children.Add(C.Space(C.Text("Another saved device uses this name. Check the key.", 12, "Danger", true), 8));
                if (FileCatalog.Risky(request.Manifest))
                    warnings.Children.Add(C.Space(C.Text("Contains programs, scripts or shortcuts. Open them only if you trust the sender.", 12, "Danger", true), 8));
                window.Content = Body(C.Stack(C.Space(C.Text("Incoming files", 23, bold: true), 14), C.Space(C.Card(C.Person(request.PeerName, request.Picture, sender, 48), 16), warnings.Children.Count > 0 ? 8 : 14), C.Space(warnings, warnings.Children.Count > 0 ? 8 : 0),
                    C.Space(C.Text($"{request.Manifest.FileCount} files · {request.Manifest.FolderCount} folders · {Format.Bytes(request.Manifest.TotalBytes)}", 13, "Muted"), 18), C.Space(C.Card(names), 18),
                    C.Space(C.Badge(request.Browser ? "Phone/browser · " + (app.Engine.Browser.Https ? "HTTPS" : "local HTTP") : request.KeyVerified ? "Paired device · public key verified" : "Connected device · saved public key", "Success", "SuccessSoft"), 16),
                    C.Text("SAVE TO", 10, "Muted", true), C.Space(location, 10, 7), C.Space(choose, 10), C.Space(free, 18), C.Text("IF FILES ALREADY EXIST", 10, "Muted", true), duplicates,
                    C.Space(C.Text(request.FixedFolder ? "Files are saved into this shared folder and become visible to its connected devices. Existing files are kept." : request.Browser ? "Browser uploads keep both copies. Files are saved atomically and never opened or shared automatically." + (app.Engine.Browser.Https ? "" : " Local HTTP is not encrypted.") : "Files are checksum-verified before saving. Received files are never opened or shared automatically.", 11, "Muted"), 20),
                    C.Inline(C.Button("Decline", () => window.Close(false)), accept)));
                CheckSpace();
                using CancellationTokenRegistration cancel = token.Register(() => Dispatcher.UIThread.Post(() => window.Close(false)));
                bool accepted = await ShowAsync<bool?>(window) == true;
                return new ReceiveDecision(accepted && !token.IsCancellationRequested, folder, (DuplicatePolicy)duplicates.SelectedIndex);
            });
        }
        finally { approvals.Release(); }
    }
    public async Task<bool> SyncAsync(SyncApproval request, CancellationToken token)
    {
        await approvals.WaitAsync(token);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                Window window = Create("Allow automatic folder sync?", 620);
                window.Content = Body(C.Stack(C.Space(C.Text(request.PeerName + " wants to sync " + request.Space.Name, 22, bold: true), 15),
                    C.Space(C.Text(request.Space.Folder, 12, "Muted"), 17),
                    C.Space(C.Text("This grants the connected device ongoing permission to add and update files in this shared folder while both apps are open. Individual sync changes will not ask again. Conflicts keep both copies; replaced versions have local backups. Deletions do not propagate.", 13), 20),
                    C.Space(C.Note("You can stop this later", "Revoke the link in Sync, change the space to Read only, stop sharing, or block the device. Existing saved copies stay on the devices."), 22),
                    C.Inline(C.Button("Decline", () => window.Close(false)), C.Button("Allow automatic sync", () => window.Close(true), true, "sync"))));
                using CancellationTokenRegistration cancel = token.Register(() => Dispatcher.UIThread.Post(() => window.Close(false)));
                return await ShowAsync<bool?>(window) == true && !token.IsCancellationRequested;
            });
        }
        finally { approvals.Release(); }
    }
    public Task DiagnosticsAsync()
    {
        Window window = Create("Connection diagnostics", 720);
        TextBox details = new()
        {
            Text = app.Engine.ConnectionDiagnostics,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 200
        };
        string firewall = OperatingSystem.IsMacOS()
            ? "macOS asks once to allow incoming connections and local network access for hopo. If you declined, open System Settings → Network → Firewall → Options, and Privacy & Security → Local Network, and allow hopo (listed as Lantoss)."
            : "If your Linux firewall is on, allow TCP 48585 and 48586 (and 80 if used) plus UDP 5353 and 48587 on your home network, for example: sudo ufw allow from 192.168.0.0/16 to any port 48585:48587 proto tcp";
        window.Content = Body(C.Stack(C.Space(C.Text("Make the connection visible.", 23, bold: true), 15), C.Space(details, 18), C.Space(C.Text(firewall, 12, "Muted"), 18),
            C.Inline(C.Button("Copy diagnostics", () => _ = app.CopyAsync(details.Text ?? "", "Diagnostics copied."), true), C.Button("Close", () => window.Close()))));
        return ShowAsync<object?>(window);
    }
}
