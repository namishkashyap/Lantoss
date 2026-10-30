using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Lantoss.Desktop;

internal sealed class SpacesPage : IPage
{
    private readonly Shell app;
    private readonly StackPanel spaces = new();
    private readonly TextBlock activity = C.Text("", 12, "Muted");
    private readonly RemoteSpacesView remote;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public Control View
    {
        get;
    }
    public SpacesPage(Shell app)
    {
        this.app = app;
        remote = new(app);
        View = C.Stack(C.Space(C.Text("A place to share, on your computer.", 28, bold: true), 10),
            C.Space(C.Text("Browse other computers' folders here, or share folders from this one. Link a space for automatic two-way sync.", 13, "Muted"), 20),
            C.Space(remote.View, 23), C.Space(C.Text("Folders shared from this " + Shell.Platform, 19, bold: true), 17),
            C.Space(C.Inline(C.AsyncButton("Create space from a folder", AddAsync, true, "plus"), C.Button("Open in browser", () => app.OpenBrowser(), icon: "phone")), 17), C.Space(activity, 21), spaces,
            C.Space(C.Note("Your folder stays on this computer", "New spaces are read only. Read & upload allows approved uploads and optional sync. Automatic sync needs a separate one-time host approval. Removing a space stops access and keeps files."), 0, 18));
        timer.Tick += (_, _) => RefreshActivity();
        View.AttachedToVisualTree += (_, _) => timer.Start();
        View.DetachedFromVisualTree += (_, _) => timer.Stop();
    }
    public void Refresh()
    {
        RefreshActivity();
        remote.RefreshPeers();
        spaces.Children.Clear();
        SharedSpace[] selected = app.Engine.Browser.Spaces.Spaces.Where(space => space.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
            spaces.Children.Add(C.Empty("Choose a folder to make your first space.", "Only the folder you select becomes available. A space stays registered across restarts, and is live while sharing is on and this computer is online.", "folder", C.AsyncButton("Choose folder", AddAsync, true, "folder")));
        foreach (SharedSpace space in selected)
        {
            bool available = SpaceCatalog.Available(space);
            ComboBox access = new()
            {
                ItemsSource = new[] { "Read only", "Read & upload" },
                SelectedIndex = (int)space.Access,
                Width = 180,
                Margin = new(0, 12, 0, 15)
            };
            access.SelectionChanged += (_, _) =>
            {
                if (access.SelectedIndex == (int)space.Access)
                    return;
                try
                {
                    app.Engine.Browser.Spaces.Update(space.Id, (SpaceAccess)access.SelectedIndex, space.Enabled);
                    app.Tell("Space permission updated. Existing transfers for this space were stopped.");
                }
                catch (Exception error) { app.Error(error); }
            };
            Button open = C.Button("Open in browser", () => app.OpenBrowser("/?space=" + Uri.EscapeDataString(space.Id)), true, "phone");
            open.IsEnabled = space.Enabled && available;
            Button reveal = C.Small("Open folder", () => app.Reveal(space.Folder));
            reveal.IsEnabled = available;
            Button toggle = C.Small(space.Enabled ? "Stop sharing" : "Enable space", () => { app.Engine.Browser.Spaces.Update(space.Id, space.Access, !space.Enabled); Refresh(); });
            Button remove = C.Small("Remove space", async () => { if (await app.Dialogs.ConfirmAsync("Remove space", "Remove this folder from Shared Spaces? Files stay on disk. Active space transfers will stop.", "Remove")) { app.Engine.Browser.Spaces.Remove(space.Id); Refresh(); } });
            string status = !space.Enabled ? "Sharing stopped" : !available ? "Folder unavailable" : app.Engine.Running ? "Shared on your network" : "Sharing is off";
            string storage = "";
            if (available)
                try
                {
                    DriveInfo drive = new(Path.GetPathRoot(space.Folder)!);
                    storage = Format.Bytes(drive.AvailableFreeSpace) + " free on the host drive";
                }
                catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { }
            Button choose = C.Small("Choose PCs…", async () =>
            {
                string[]? peers = await app.Dialogs.ChoosePeersAsync("Who can open " + space.Name, space.AllowedPeers);
                if (peers == null)
                    return;
                app.Engine.Browser.Spaces.SetAllowed(space.Id, peers.Length == 0 ? null : peers);
                Refresh();
                app.Tell(peers.Length == 0 ? "Every paired PC can open this space." : "Only the chosen PCs can open this space.");
            });
            int allowed = space.AllowedPeers?.Length ?? 0;
            TextBlock who = C.Text("Who can open: " + (allowed == 0 ? "All paired PCs" : allowed == 1 ? "1 PC" : allowed + " PCs"), 12, "Muted");
            who.VerticalAlignment = VerticalAlignment.Center;
            StackPanel body = C.Stack(C.Heading(space.Name, C.Badge(status, space.Enabled && available ? "Success" : "Muted", space.Enabled && available ? "SuccessSoft" : "Background"), 17),
                C.Space(C.Text("Host: " + app.Store.Preferences.DeviceName, 12, "Muted"), 6), C.Space(C.Text(space.Folder, 11, "Muted"), 8), C.Text(storage, 11, "Muted"), access, C.Space(C.Inline(who, choose), 12), C.Wrap(open, reveal, toggle, remove));
            spaces.Children.Add(C.Space(C.Card(body), 16));
        }
    }
    private void RefreshActivity()
    {
        TransferRecord[] jobs = app.Engine.Transfers.Records.Where(record => record.SpaceId.Length > 0 && !record.Terminal).ToArray();
        activity.Text = jobs.Length == 0 ? "No active space transfers. Live MB/s appears while files move." : $"{jobs.Length} active space transfers · {jobs.Sum(record => record.BytesPerSecond) / 1048576:0.00} MB/s";
    }
    private async Task AddAsync()
    {
        string[] picked = await app.PickFoldersAsync("Choose a local folder for a Shared Space");
        if (picked.Length == 0)
            return;
        app.Engine.Browser.Spaces.Add(picked[0]);
        Refresh();
        app.Tell("Read-only space created. Connected computers can browse it in Shared Spaces. Enable Read & upload to allow uploads or sync.");
    }
}

/// <summary>Native remote-folder browser. Only space IDs and relative paths cross the network; the remote root is never shown as a path.</summary>
internal sealed class RemoteSpacesView
{
    private readonly Shell app;
    private readonly ComboBox devices = new() { MinWidth = 240, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel contents = new();
    private readonly TextBlock status = C.Text("Connect another computer to browse its shared folders here.", 12, "Muted");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(12) };
    private Peer? peer;
    private RemoteSpace? selected;
    private string folder = "";
    private int generation;
    private bool loading, active, reloadPending;
    public Control View
    {
        get;
    }
    public RemoteSpacesView(Shell app)
    {
        this.app = app;
        View = C.Card(C.Stack(C.Heading("Shared folders on your other computers"),
            C.Space(C.Text("Browse, download, upload, or link a folder for automatic two-way sync. All computer-to-computer traffic uses TLS.", 12, "Muted"), 14),
            C.Space(C.Inline(devices, C.AsyncButton("Refresh folders", RefreshCurrentAsync, icon: "sync")), 14), C.Space(status, 14), contents));
        devices.SelectionChanged += async (_, _) => { generation++; peer = devices.SelectedItem as Peer; selected = null; folder = ""; contents.Children.Clear(); if (active) await RefreshCurrentAsync(); };
        View.AttachedToVisualTree += async (_, _) => { active = !app.Preview; RefreshPeers(); if (!active) return; timer.Start(); await LoadSpacesAsync(); };
        View.DetachedFromVisualTree += (_, _) => { active = false; generation++; reloadPending = false; timer.Stop(); };
        timer.Tick += async (_, _) => { if (!loading && active) { if (selected == null) await LoadSpacesAsync(); else await BrowseAsync(selected, folder); } };
    }
    public void RefreshPeers()
    {
        Peer[] peers = app.Engine.Peers.Where(item => item.Paired && !item.Blocked).ToArray();
        devices.IsEnabled = peers.Length > 0;
        string? key = (devices.SelectedItem as Peer)?.Fingerprint;
        if ((devices.ItemsSource as Peer[])?.Select(item => item.Fingerprint + item.Endpoint).SequenceEqual(peers.Select(item => item.Fingerprint + item.Endpoint)) == true)
            return;
        devices.ItemsSource = peers;
        devices.SelectedItem = peers.FirstOrDefault(item => item.Fingerprint == key) ?? peers.FirstOrDefault();
    }
    private Task RefreshCurrentAsync()
    {
        if (loading)
        {
            reloadPending = true;
            return Task.CompletedTask;
        }
        return selected == null ? LoadSpacesAsync() : BrowseAsync(selected, folder);
    }
    private async Task LoadSpacesAsync()
    {
        if (peer == null)
        {
            contents.Children.Clear();
            status.Text = "Waiting for a connected computer. Open Lantoss on both.";
            return;
        }
        if (loading || !active)
            return;
        int load = ++generation;
        loading = true;
        Peer target = peer;
        status.Text = "Reading shared folders from " + target.Name + "…";
        try
        {
            RemoteSpace[] spaces = await app.Engine.GetRemoteSpacesAsync(target);
            if (!active || load != generation)
                return;
            selected = null;
            folder = "";
            contents.Children.Clear();
            status.Text = spaces.Length == 0 ? "No folders shared yet. On that computer, create a folder in Shared Spaces." : target.Name + " · " + spaces.Length + " shared folders";
            foreach (RemoteSpace space in spaces)
            {
                Button browse = C.AsyncButton("Browse folder", () => BrowseAsync(space, ""), true, "folder");
                browse.IsEnabled = space.Available;
                Button sync = C.AsyncButton("Set up automatic sync", () => LinkAsync(target, space), icon: "sync");
                sync.IsEnabled = space.Available && space.CanUpload;
                ToolTip.SetTip(sync, space.CanUpload ? "Choose a local folder; approve once on the host." : "The host must enable Read & upload before two-way sync.");
                contents.Children.Add(C.Space(C.Stack(C.Heading(space.Name, C.Badge(!space.Available ? "Unavailable" : space.CanUpload ? "Read & upload" : "Read only"), 14), C.Inline(browse, sync)), 16));
            }
        }
        catch (Exception error) { if (load == generation) status.Text = TransferEngine.HumanError(error) + " Refresh retries automatically."; }
        finally { loading = false; if (reloadPending && active) { reloadPending = false; await RefreshCurrentAsync(); } }
    }
    private async Task BrowseAsync(RemoteSpace space, string path)
    {
        if (peer == null || !active)
            return;
        int load = ++generation;
        loading = true;
        Peer target = peer;
        try
        {
            SpaceEntry[] entries = await app.Engine.BrowseRemoteSpaceAsync(target, space.Id, path);
            if (!active || load != generation)
                return;
            selected = space;
            folder = path;
            contents.Children.Clear();
            status.Text = target.Name + " / " + space.Name + (path.Length > 0 ? " / " + path : "");
            Button up = C.AsyncButton("Up one folder", () => BrowseAsync(space, path.Contains('/') ? path[..path.LastIndexOf('/')] : ""));
            up.IsEnabled = path.Length > 0;
            Button upload = C.AsyncButton("Upload files here", async () =>
            {
                string[] files = await app.PickFilesAsync("Upload files to " + space.Name);
                if (files.Length == 0)
                    return;
                status.Text = "Waiting for approval on " + target.Name + "…";
                await app.Engine.UploadToSpaceAsync(target, space.Id, path, files);
                app.Tell("Upload finished. See Transfers for the result.");
                await BrowseAsync(space, path);
            }, true, "send");
            upload.IsEnabled = space.CanUpload;
            contents.Children.Add(C.Space(C.Wrap(C.AsyncButton("All shared folders", LoadSpacesAsync), up, upload), 9));
            Button sync = C.AsyncButton("Sync this space with a local folder", () => LinkAsync(target, space), icon: "sync");
            sync.IsEnabled = space.CanUpload;
            contents.Children.Add(C.Space(sync, 17));
            if (entries.Length == 0)
                contents.Children.Add(C.Text("This folder is empty.", 12, "Muted"));
            foreach (SpaceEntry entry in entries)
            {
                Button action = entry.Directory ? C.AsyncButton("Open", () => BrowseAsync(space, entry.Path), icon: "folder") : C.AsyncButton("Download", async () =>
                {
                    string inbox = app.Store.Preferences.ReceiveFolder;
                    FileCatalog.CheckSafeDirectory(inbox);
                    Directory.CreateDirectory(inbox);
                    await app.Engine.DownloadSpaceFileAsync(target, space.Id, entry.Path, inbox);
                    app.Tell("Downloaded to your inbox. Existing files are kept.");
                }, icon: "download");
                contents.Children.Add(C.Space(C.Heading((entry.Directory ? "Folder · " : "") + entry.Name + (entry.Directory ? "" : " · " + Format.Bytes(entry.Size)), action, 12), 8));
            }
        }
        catch (Exception error) { if (load == generation) status.Text = TransferEngine.HumanError(error); }
        finally { loading = false; if (reloadPending && active) { reloadPending = false; await RefreshCurrentAsync(); } }
    }
    private async Task LinkAsync(Peer target, RemoteSpace space)
    {
        string[] picked = await app.PickFoldersAsync("Choose a local folder for two-way sync with " + space.Name);
        if (picked.Length == 0)
            return;
        if (!await app.Dialogs.ConfirmAsync("Set up automatic sync", "Link this local folder with " + space.Name + " on " + target.Name + "?\n\nAdded and changed files sync both ways while both apps are open. Different same-name files keep a conflict copy. Deleted files are restored from the remaining copy; deletions do not propagate. The host must approve once.\n\n" + picked[0], "Link folder"))
            return;
        status.Text = "Approve automatic sync on " + target.Name + "…";
        await app.Engine.LinkSpaceAsync(target, space, picked[0]);
        app.Navigate("Sync");
        app.Tell("Folder linked. Changes now sync automatically while both computers keep Lantoss open.");
    }
}

internal sealed class SyncPage : IPage
{
    private readonly Shell app;
    private readonly StackPanel links = new();
    public Control View
    {
        get;
    }
    public SyncPage(Shell app)
    {
        this.app = app;
        View = C.Stack(C.Space(C.Text("Keep your folders together.", 28, bold: true), 10),
            C.Space(C.Text("Low-impact automatic sync. Changed files update in the background while both computers keep Lantoss open. Sync now runs a full checksum check.", 13, "Muted"), 20),
            C.Space(C.Button("Choose another computer's shared folder", () => app.Navigate("Shared Spaces"), true, "folder"), 22), links,
            C.Space(C.Note("Conflicts and earlier versions are kept", "New and changed files sync both ways. Different same-name files keep a conflict copy on both computers. Replaced versions are kept in the destination's .flowshare-versions folder. Deletions stay local and the remaining copy can restore them. Backups are not pruned automatically."), 0, 18));
    }
    public void Refresh()
    {
        links.Children.Clear();
        foreach (SyncLink link in app.Engine.Sync.Headers)
        {
            SyncStatus? status = app.Engine.SyncStatuses.FirstOrDefault(item => item.Id == link.Id);
            Button now = C.AsyncButton("Sync now", () => app.Engine.SyncOnceAsync(link.Id), true, "sync");
            now.IsEnabled = link.Enabled;
            links.Children.Add(C.Space(C.Card(C.Stack(C.Heading(link.Name, C.Badge(link.Enabled ? "Two-way sync" : "Paused")),
                C.Space(C.Text(link.Folder, 11, "Muted"), 10), C.Space(C.Text(status?.Message ?? "Waiting for connection", 12, "Primary"), 9),
                C.Space(C.Text(link.LastSuccessUtc is DateTime last ? "Last successful update: " + last.ToLocalTime().ToString("dd MMM, HH:mm") : "First sync pending", 11, "Muted"), 16),
                C.Wrap(C.Button(link.Enabled ? "Pause sync" : "Resume sync", () => { app.Engine.PauseSync(link.Id, link.Enabled); Refresh(); }), now, C.Small("Open local folder", () => app.Reveal(link.Folder)),
                    C.Small("Remove link", async () => { if (await app.Dialogs.ConfirmAsync("Remove sync link", "Stop automatic sync for this folder? Local files and backups stay on both computers. The host can revoke its permission under Sync.", "Remove")) { app.Engine.RemoveSync(link.Id); Refresh(); } })))), 17));
        }
        if (app.Engine.Sync.Headers.Length == 0)
            links.Children.Add(C.Space(C.Empty("No linked folders yet.", "In Shared Spaces, browse another computer and choose Set up automatic sync. On that computer, enable Read & upload for the space, then approve the sync request once.", "sync"), 17));
        SyncGrant[] grants = app.Engine.Sync.Grants;
        if (grants.Length > 0)
            links.Children.Add(C.Space(C.Text("Other computers allowed to sync folders here", 17, bold: true), 16));
        foreach (SyncGrant grant in grants)
        {
            string peer = app.Store.Peers.FirstOrDefault(item => item.Fingerprint == grant.PeerKey)?.Name ?? "Disconnected computer";
            string space = app.Store.Spaces.FirstOrDefault(item => item.Id == grant.SpaceId)?.Name ?? "Removed space";
            links.Children.Add(C.Space(C.Card(C.Heading(peer + " → " + space, C.Small("Revoke sync access", () => { app.Engine.Sync.Revoke(grant.Id, grant.PeerKey); Refresh(); app.Tell("Automatic writes from this link are no longer allowed. Existing files stay."); }), 13)), 12));
        }
    }
}
