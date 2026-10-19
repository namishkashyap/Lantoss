using Microsoft.Win32;
using System.Windows.Threading;

// Native remote-folder browser. Public space IDs and relative paths cross the network; the remote filesystem root is never exposed as a UI path.
namespace Lantoss.UI;

internal sealed class RemoteSpacesView
{
    private readonly AppShell app;
    private readonly ComboBox devices = new() { MinWidth = 230, MaxWidth = 340, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel contents = new();
    private readonly TextBlock status = C.Text("Connect another PC to browse its shared folders here.", 12, "Muted");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(12) };
    private Peer? peer;
    private RemoteSpace? selected;
    private string folder = "";
    private int generation;
    private bool loading, active;
    public FrameworkElement View
    {
        get;
    }
    public RemoteSpacesView(AppShell app)
    {
        this.app = app;
        devices.ItemTemplate = (DataTemplate)app.Window.FindResource("DeviceNameTemplate");
        Button reload = C.AsyncButton("Refresh folders", LoadSpacesAsync, icon: "sync");
        reload.Margin = new(10, 0, 0, 0);
        reload.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(devices, "Connected PC");
        View = C.Card(C.Stack(C.Heading("Shared folders on your other PCs"),
            C.Space(C.Text("Browse, download, upload, or link a folder for automatic two-way sync. All PC-to-PC traffic uses TLS.", 12, "Muted"), 14),
            C.Space(C.Inline(devices, reload), 14), C.Space(status, 14), contents));
        devices.SelectionChanged += async (_, _) => { generation++; peer = devices.SelectedItem as Peer; selected = null; folder = ""; contents.Children.Clear(); if (active) await LoadSpacesAsync(); };
        View.Loaded += async (_, _) => { active = !app.Preview; RefreshPeers(); if (!active) return; timer.Start(); await LoadSpacesAsync(); };
        View.Unloaded += (_, _) => { active = false; generation++; timer.Stop(); };
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
    private async Task LoadSpacesAsync()
    {
        if (peer == null)
        {
            contents.Children.Clear();
            status.Text = "Waiting for a connected PC. Open Lantoss 1.6 on both PCs.";
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
            status.Text = spaces.Length == 0 ? "No folders shared yet. On that PC, create a folder in Shared Spaces." : target.Name + " · " + spaces.Length + " shared folders";
            foreach (RemoteSpace space in spaces)
            {
                Button browse = C.AsyncButton("Browse folder", () => BrowseAsync(space, ""), true, "folder");
                browse.IsEnabled = space.Available;
                Button sync = C.AsyncButton("Set up automatic sync", () => LinkAsync(target, space), icon: "sync");
                sync.IsEnabled = space.Available && space.CanUpload;
                sync.Margin = new(9, 0, 0, 0);
                sync.ToolTip = space.CanUpload ? "Choose a local folder; approve once on the host PC." : "The host must enable Read & upload before two-way sync.";
                contents.Children.Add(C.Space(C.Stack(C.Heading(space.Name, C.Badge(!space.Available ? "Unavailable" : space.CanUpload ? "Read & upload" : "Read only"), 14), C.Inline(browse, sync)), 16));
            }
        }
        catch (Exception error) { if (load == generation) status.Text = TransferEngine.HumanError(error) + " Refresh retries automatically."; }
        finally { loading = false; }
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
            Button back = C.AsyncButton("All shared folders", LoadSpacesAsync);
            Button up = C.AsyncButton("Up one folder", () => BrowseAsync(space, path.Contains('/') ? path[..path.LastIndexOf('/')] : ""));
            up.IsEnabled = path.Length > 0;
            up.Margin = new(8, 0, 0, 0);
            Button upload = C.AsyncButton("Upload files here", async () =>
            {
                OpenFileDialog picker = new()
                {
                    Multiselect = true,
                    Title = "Upload files to " + space.Name
                };
                if (picker.ShowDialog(app.Window) != true)
                    return;
                status.Text = "Waiting for approval on " + target.Name + "…";
                await app.Engine.UploadToSpaceAsync(target, space.Id, path, picker.FileNames);
                app.Tell("Upload finished. See Transfers for the result.");
                await BrowseAsync(space, path);
            }, true, "send");
            upload.IsEnabled = space.CanUpload;
            upload.Margin = new(8, 0, 0, 0);
            contents.Children.Add(C.Space(C.Inline(back, up, upload), 15));
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
        finally { loading = false; }
    }
    private async Task LinkAsync(Peer target, RemoteSpace space)
    {
        OpenFolderDialog picker = new()
        {
            Title = "Choose a local folder for two-way sync with " + space.Name
        };
        if (picker.ShowDialog(app.Window) != true)
            return;
        if (MessageBox.Show(app.Window, "Link this local folder with " + space.Name + " on " + target.Name + "?\n\nAdded and changed files will sync both ways while both apps are open. Different same-name files keep a conflict copy. Deleted files are restored from the remaining copy; deletions do not propagate. The host must approve once.\n\n" + picker.FolderName, "Set up automatic sync", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        status.Text = "Approve automatic sync on " + target.Name + "…";
        await app.Engine.LinkSpaceAsync(target, space, picker.FolderName);
        app.Navigate("Sync");
        app.Tell("Folder linked. Changes now sync automatically while both PCs keep Lantoss open.");
    }
}
