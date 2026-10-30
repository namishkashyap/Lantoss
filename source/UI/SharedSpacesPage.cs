using Microsoft.Win32;
using System.Windows.Threading;

namespace Lantoss.UI;

internal sealed class SharedSpacesPage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel spaces = new();
    private readonly TextBlock activity = C.Text("", 12, "Muted");
    private readonly RemoteSpacesView remote;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public FrameworkElement View
    {
        get;
    }
    public SharedSpacesPage(AppShell app)
    {
        this.app = app;
        remote = new(app);
        Button add = C.Button("Create space from a folder", Add, true, "plus");
        Button browser = C.Button("Open in browser", () => app.OpenBrowser(), icon: "devices");
        browser.Margin = new(10, 0, 0, 0);
        View = C.Stack(C.Space(C.Text("A place to share, on your PC.", 26, bold: true), 10),
            C.Space(C.Text("Browse the other PC's folders here, or share folders from this PC. Link a space for automatic two-way sync.", 13, "Muted"), 20),
            C.Space(remote.View, 23), C.Space(C.Text("Folders shared from this PC", 19, bold: true), 17),
            C.Space(C.Inline(add, browser), 17), C.Space(activity, 21), spaces,
            C.Space(C.Note("Your folder stays on this PC", "New spaces are read only. Read & upload allows approved uploads and optional sync. Automatic sync needs a separate one-time host approval. Removing a space stops access and keeps files. Per-person team roles are not included."), 0, 18));
        timer.Tick += (_, _) => RefreshActivity();
        View.Loaded += (_, _) => timer.Start();
        View.Unloaded += (_, _) => timer.Stop();
    }
    public void Refresh()
    {
        RefreshActivity();
        remote.RefreshPeers();
        spaces.Children.Clear();
        SharedSpace[] selected = app.Engine.Browser.Spaces.Spaces.Where(space => space.Name.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
            spaces.Children.Add(C.Empty("Choose a folder to make your first space.", "Only the folder you select becomes available. A space remains registered across restarts, and is live while sharing is enabled and this PC is online.", "folder", C.Button("Choose folder", Add, true, "folder")));
        foreach (SharedSpace space in selected)
        {
            bool available = SpaceCatalog.Available(space);
            ComboBox access = new()
            {
                ItemsSource = new[] { "Read only", "Read & upload" },
                SelectedIndex = (int)space.Access,
                Width = 170,
                Margin = new(0, 12, 0, 15)
            };
            System.Windows.Automation.AutomationProperties.SetName(access, space.Name + " access permission");
            access.SelectionChanged += (_, _) => { try { app.Engine.Browser.Spaces.Update(space.Id, (SpaceAccess)access.SelectedIndex, space.Enabled); app.Tell("Space permission updated. Existing transfers for this space were stopped."); } catch (Exception error) { app.Error(error); } };
            Button open = C.Button("Open in browser", () => app.OpenBrowser("/?space=" + Uri.EscapeDataString(space.Id)), true, "devices");
            open.IsEnabled = space.Enabled && available;
            Button reveal = C.Small("Open folder", () => app.Reveal(space.Folder));
            reveal.Margin = new(8, 0, 0, 0);
            reveal.IsEnabled = available;
            Button toggle = C.Small(space.Enabled ? "Stop sharing" : "Enable space", () => { app.Engine.Browser.Spaces.Update(space.Id, space.Access, !space.Enabled); Refresh(); });
            toggle.Margin = new(8, 0, 0, 0);
            Button remove = C.Small("Remove space", () => { if (MessageBox.Show(app.Window, "Remove this folder from Shared Spaces? Files will stay on disk. Active space transfers will stop.", "Remove space", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) { app.Engine.Browser.Spaces.Remove(space.Id); Refresh(); } });
            remove.Margin = new(8, 0, 0, 0);
            string status = !space.Enabled ? "Sharing stopped" : !available ? "Folder unavailable" : app.Engine.Running ? "Shared on your network" : "Sharing is off";
            string storage = "";
            if (available)
            {
                try
                {
                    DriveInfo drive = new(Path.GetPathRoot(space.Folder)!);
                    storage = Format.Bytes(drive.AvailableFreeSpace) + " free on the host drive";
                }
                catch (IOException) { }
            }
            StackPanel body = C.Stack(C.Heading(space.Name, C.Badge(status, space.Enabled && available ? "Success" : "Muted", space.Enabled && available ? "SuccessSoft" : "Background"), 17),
                C.Space(C.Text("Host: " + app.Store.Preferences.DeviceName, 12, "Muted"), 6), C.Space(C.Text(space.Folder, 11, "Muted"), 8), C.Text(storage, 11, "Muted"), access, C.Space(Audience(space), 15), C.Inline(open, reveal, toggle, remove));
            spaces.Children.Add(C.Space(C.Card(body), 16));
        }
    }
    /// <summary>Who can open the space: every paired PC, or only the ticked ones. Phones need Allow on this PC first.</summary>
    private Expander Audience(SharedSpace space)
    {
        Peer[] paired = app.Store.Peers.Where(peer => peer.Paired && !peer.Blocked).OrderBy(peer => peer.Name).ToArray();
        CheckBox everyone = new()
        {
            Content = "All paired PCs",
            IsChecked = space.AllowedPeers is not { Length: > 0 },
            Margin = new(0, 8, 0, 6)
        };
        CheckBox[] choices = paired.Select(peer => new CheckBox { Content = peer.Name + " · " + Format.ShortKey(peer.Fingerprint), Tag = peer.Fingerprint, IsChecked = space.AllowedPeers?.Contains(peer.Fingerprint) == true, IsEnabled = space.AllowedPeers is { Length: > 0 }, Margin = new(18, 0, 0, 6) }).ToArray();
        void Save()
        {
            foreach (CheckBox choice in choices)
                choice.IsEnabled = everyone.IsChecked != true;
            string[] keys = everyone.IsChecked == true ? [] : choices.Where(choice => choice.IsChecked == true).Select(choice => (string)choice.Tag).ToArray();
            if (everyone.IsChecked != true && keys.Length == 0)
            {
                app.Tell("Tick at least one PC, or choose All paired PCs.");
                return;
            }
            try
            {
                app.Engine.Browser.Spaces.SetAllowed(space.Id, keys);
                app.Tell("Space access updated. PCs that are no longer allowed lose access now.");
            }
            catch (Exception error) { app.Error(error); }
        }
        everyone.Click += (_, _) => Save();
        foreach (CheckBox choice in choices)
            choice.Click += (_, _) => Save();
        StackPanel list = C.Stack(everyone);
        foreach (CheckBox choice in choices)
            list.Children.Add(choice);
        if (paired.Length == 0)
            list.Children.Add(C.Text("No paired PCs yet.", 11, "Muted"));
        Expander expander = new()
        {
            Header = "Who can open · " + (space.AllowedPeers is { Length: > 0 } chosen ? chosen.Length + (chosen.Length == 1 ? " PC" : " PCs") : "All paired PCs"),
            Content = list
        };
        expander.SetResourceReference(Control.ForegroundProperty, "Text");
        return expander;
    }
    private void RefreshActivity()
    {
        TransferRecord[] jobs = app.Engine.Transfers.Records.Where(record => record.SpaceId.Length > 0 && !record.Terminal).ToArray();
        double speed = jobs.Sum(record => record.BytesPerSecond);
        activity.Text = jobs.Length == 0 ? "No active space transfers. Live MB/s appears while files move." : $"{jobs.Length} active space transfers · {speed / 1048576:0.00} MB/s";
    }
    private void Add()
    {
        OpenFolderDialog picker = new()
        {
            Title = "Choose a local folder for a Shared Space"
        };
        if (picker.ShowDialog(app.Window) != true)
            return;
        try
        {
            app.Engine.Browser.Spaces.Add(picker.FolderName);
            Refresh();
            app.Tell("Read-only space created. Connected PCs can browse it inside Shared Spaces. Enable Read & upload to allow uploads or sync.");
        }
        catch (Exception error) { app.Error(error); }
    }
}
