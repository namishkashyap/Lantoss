using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Lantoss.UI;

internal interface IPage
{
    FrameworkElement View
    {
        get;
    }
    void Refresh();
}
/// <summary>
/// Owns navigation, semantic theme resources and engine lifetime. Engine events can arrive off-thread: marshal UI updates through Ui rather than touching WPF controls directly.
/// </summary>
internal sealed class AppShell : IDisposable
{
    public static AppShell Current { get; private set; } = null!;
    public Window Window
    {
        get;
    }
    public MetadataStore Store
    {
        get;
    }
    public TransferEngine Engine
    {
        get;
    }
    public List<SelectionItem> Selection { get; } = [];
    public string Search => Find<TextBox>("SearchBox").Text.Trim();
    public string? SelectedPeerKey
    {
        get; set;
    }
    public string CurrentPage { get; private set; } = "Home";
    public Dialogs Dialogs
    {
        get;
    }
    public PrivateFirewallSetup Firewall
    {
        get;
    }
    public WindowsIntegration? Integration
    {
        get;
    }
    private readonly ExplorerSharing explorerSharing;
    private bool exitRequested, trayNoticeShown;
    private readonly Identity identity;
    private readonly bool preview;
    public bool Preview => preview;
    private readonly Dictionary<string, Button> navigation = [];
    private readonly Dictionary<string, string> navigationIcons = [];
    private readonly Forms.NotifyIcon? notifications;
    private readonly System.Drawing.Icon? trayIcon;
    private IPage? page;
    private bool selecting, disposed, closing, allowClose;
    private bool sharingStoppedManually;
    private string networkSignature = "";
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    public AppShell(MetadataStore store, Identity identity, bool preview)
    {
        Store = store;
        this.identity = identity;
        this.preview = preview;
        using Stream xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lantoss.Shell.xaml") ?? throw new InvalidDataException("The app shell is missing.");
        Window = (Window)XamlReader.Load(xaml);
        Current = this;
        Engine = new(store, identity, allowLoopback: preview);
        Dialogs = new(this);
        Firewall = new(this);
        explorerSharing = new(this);
        if (!preview)
            Integration = new();
        using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lantoss.ico")!)
            Window.Icon = BitmapFrame.Create(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (!preview)
        {
            Window.MinWidth = Math.Min(1000, Math.Max(760, SystemParameters.WorkArea.Width - 20));
            Window.MinHeight = Math.Min(720, Math.Max(520, SystemParameters.WorkArea.Height - 20));
            Window.Width = Math.Min(Window.Width, SystemParameters.WorkArea.Width - 20);
            Window.Height = Math.Min(900, SystemParameters.WorkArea.Height - 20);
        }
        if (preview)
            Find<TextBlock>("ModeBadge").Text = "PREVIEW";
        Engine.ApprovePair = Dialogs.CompareAsync;
        Engine.ApproveReceive = (request, token) => { Ui(() => NotifyIncoming(request)); return Dialogs.ReceiveAsync(request, token); };
        Engine.ApproveSync = Dialogs.SyncAsync;
        Engine.SyncChanged += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Browser.Notes.Changed += () => Ui(() => { if (CurrentPage == "Text & links") page?.Refresh(); });
        Engine.Sync.Changed += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Status += message => Ui(() => Tell(message));
        Engine.PeersChanged += () => Ui(() => { RefreshExplorerMenu(); if (CurrentPage is "Home" or "Devices" or "Send" or "Shared Spaces") page?.Refresh(); });
        Engine.Browser.Changed += () => Ui(() => { if (CurrentPage is "Phone & browser" or "Shared Spaces" or "Devices" or "Home") page?.Refresh(); if (CurrentPage is "Send") page?.Refresh(); if (!preview && Engine.Running) _ = Firewall.EnsureAsync(); });
        Engine.Browser.ApprovalRequested += device => Ui(() => NotifyWaiting(device));
        Engine.Transfers.Changed += record => Ui(() => { if (CurrentPage is "Home" or "Transfers" or "History") page?.Refresh(); });
        Engine.Transfers.PersistenceFailed += _ => Ui(() => Tell("History could not be saved. Check free space and permissions in the app's local metadata folder."));
        Engine.Completed += record => Ui(() =>
        {
            if (notifications != null && Store.Preferences.Notifications)
            {
                notifications.BalloonTipTitle = record.Transport.Contains("HTTP", StringComparison.Ordinal) ? record.Direction == "Received" ? "Browser files saved" : "Files sent to browser" : record.Direction == "Received" ? "Files received and verified" : "Transfer verified";
                notifications.BalloonTipText = $"{record.FileCount} files · {record.PeerName}";
                notifications.ShowBalloonTip(5000);
            }
        });
        if (!preview)
        {
            using Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lantoss.ico")!;
            using System.Drawing.Icon asset = new(iconStream);
            trayIcon = (System.Drawing.Icon)asset.Clone();
            notifications = new Forms.NotifyIcon { Icon = trayIcon, Text = "Lantoss — local file sharing", Visible = true };
            notifications.DoubleClick += (_, _) => ShowWindow();
            notifications.BalloonTipClicked += (_, _) => Ui(ShowWindow);
            Forms.ContextMenuStrip tray = new();
            tray.Items.Add("Open Lantoss", null, (_, _) => Ui(ShowWindow));
            tray.Items.Add("Start sharing server", null, (_, _) => Ui(() => { try { if (!Engine.Running) ToggleReceiving(); } catch (Exception error) { Error(error); } }));
            tray.Items.Add("Stop sharing server", null, (_, _) => Ui(() => { if (Engine.Running) ToggleReceiving(); }));
            tray.Items.Add(new Forms.ToolStripSeparator());
            tray.Items.Add("Exit Lantoss", null, (_, _) => Ui(() => { exitRequested = true; Window.Close(); }));
            notifications.ContextMenuStrip = tray;
            RefreshExplorerMenu();
        }
        string[] names = ["Home", "Send", "Transfers", "Devices", "Phone & browser", "Text & links", "Shared Spaces", "Sync", "History", "Settings"];
        string[] icons = ["home", "send", "transfer", "devices", "devices", "file", "folder", "sync", "history", "settings"];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            Button button = C.Button(name, () => Navigate(name));
            navigationIcons[name] = icons[i];
            button.Content = NavigationContent(name, false);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new(0, 0, 0, 4);
            button.Padding = new(12, 9, 12, 9);
            button.BorderThickness = new(0);
            navigation.Add(name, button);
            Find<StackPanel>("Navigation").Children.Add(button);
        }
        Find<TextBox>("SearchBox").TextChanged += (_, _) => page?.Refresh();
        Find<Button>("ReceiveToggle").Click += (_, _) => { try { ToggleReceiving(); } catch (Exception e) { Error(e); } };
        Find<Button>("OpenBrowser").Click += (_, _) => { try { OpenBrowser(); } catch (Exception error) { Error(error); } };
        Window.Closing += OnClosing;
        if (!preview)
        {
            networkTimer.Tick += (_, _) => RecoverNetwork();
            SystemEvents.UserPreferenceChanged += SystemThemeChanged;
            Window.Loaded += (_, _) =>
            {
                networkTimer.Start();
                if (!Store.Preferences.StartReceivingOnOpen || Engine.Running)
                    return;
                try
                {
                    ToggleReceiving();
                }
                catch (Exception error) { Tell("Nearby connection could not start. " + TransferEngine.HumanError(error)); }
            };
        }
        Find<ComboBox>("AppearanceSelector").ItemsSource = Appearance.Modes;
        ApplyTheme(store.Preferences.Theme);
        Find<ComboBox>("AppearanceSelector").SelectionChanged += (_, _) =>
        {
            if (Find<ComboBox>("AppearanceSelector").SelectedItem is string mode && mode != Store.Preferences.Theme)
                try
                {
                    SetAppearance(mode);
                }
                catch (Exception error) { ApplyTheme(Store.Preferences.Theme); Error(error); }
        };
        UpdateStatus();
        Navigate("Home");
        if (store.RecoveryNotice != null)
            Tell(store.RecoveryNotice);
    }
    public void ShowWindow()
    {
        Window.Show();
        Window.WindowState = WindowState.Normal;
        Window.Activate();
    }
    public void AcceptLaunch(LaunchRequest request) => explorerSharing.Accept(request);
    public void RefreshExplorerMenu()
    {
        if (preview || Integration == null)
            return;
        try
        {
            Integration.Refresh(Store.Peers);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Tell("Explorer integration needs attention: " + error.Message); }
    }
    public async Task PrepareExplorerShareAsync(IEnumerable<string> paths, string? peerKey)
    {
        if (selecting)
            throw new IOException("Finish the current file selection before sharing another Explorer batch.");
        string[] selected = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Peer? direct = Store.Peers.FirstOrDefault(peer => peer.Fingerprint == peerKey && peer.Paired && !peer.Blocked);
        if (direct != null)
        {
            // Choosing a named PC in Explorer is the sender's explicit send action.
            // Queue through the normal pinned-TLS/receiver-approval path; preserve any unsent draft.
            if (!Engine.Running && !preview)
                ToggleReceiving();
            _ = Engine.Send(direct, selected);
            Navigate("Transfers");
            Tell("Explorer transfer queued for " + direct.Name + ". The receiver must approve the files.");
            return;
        }
        // Keep existing draft files unless the user explicitly replaces them.
        if (Selection.Count > 0 && !preview && MessageBox.Show(Window, "Replace the current unsent file selection with the files selected in Explorer?", "Share with Lantoss", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _ = await Task.Run(() => FileCatalog.BuildAsync(selected, Guid.NewGuid().ToString("N"), false, default));
        if (!Engine.Running && !preview)
            ToggleReceiving();
        Selection.Clear();
        await AddSelectionsAsync(selected);
        Peer? target = Store.Peers.FirstOrDefault(peer => peer.Fingerprint == peerKey && peer.Paired && !peer.Blocked);
        SelectedPeerKey = target?.Fingerprint;
        Navigate("Send");
        Tell(target != null ? "Ready for " + target.Name + ". Review the files, then click Send securely." : peerKey == null ? "Files from Explorer are ready. Choose a PC and click Send securely." : "That PC is no longer paired or is blocked. Choose another connected PC.");
    }
    public T Find<T>(string name) where T : class => Window.FindName(name) as T ?? throw new InvalidDataException("Missing UI control: " + name);
    public void Ui(Action action)
    {
        if (!disposed && !Window.Dispatcher.HasShutdownStarted)
            Window.Dispatcher.BeginInvoke(action);
    }
    public void Tell(string message) => Find<TextBlock>("Feedback").Text = message;
    public void Error(Exception error)
    {
        Tell(TransferEngine.HumanError(error));
        if (!preview)
            MessageBox.Show(Window, TransferEngine.HumanError(error), "Lantoss", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private UIElement NavigationContent(string name, bool active)
    {
        FrameworkElement glyph = C.Icon(navigationIcons[name], 18, active ? "OnAccent" : "Primary");
        glyph.Margin = new(0, 0, 14, 0);
        return C.Inline(glyph, C.Text(name, 12, active ? "OnAccent" : "Text", active));
    }
    public void Navigate(string destination)
    {
        CurrentPage = destination;
        page = null;
        Find<TextBox>("SearchBox").Text = "";
        Find<TextBlock>("PageTitle").Text = destination;
        Find<TextBox>("SearchBox").IsEnabled = destination is "Home" or "Send" or "Transfers" or "Devices" or "History" or "Shared Spaces" or "Text & links";
        foreach ((string name, Button button) in navigation)
        {
            button.SetResourceReference(Control.BackgroundProperty, name == destination ? "Accent" : "Sidebar");
            button.Content = NavigationContent(name, name == destination);
        }
        page = destination switch
        {
            "Home" => new HomePage(this),
            "Send" => new SendPage(this),
            "Transfers" => new TransfersPage(this),
            "Devices" => new DevicesPage(this),
            "Phone & browser" => new PhoneBrowserPage(this),
            "Text & links" => new NotesPage(this),
            "Shared Spaces" => new SharedSpacesPage(this),
            "Sync" => new SyncPage(this),
            "History" => new HistoryPage(this),
            "Settings" => new SettingsPage(this),
            _ => new ComingSoonPage(this, destination)
        };
        Find<ContentControl>("PageContent").Content = page.View;
        Find<ScrollViewer>("PageScroll").ScrollToTop();
        page.Refresh();
    }
    public void RefreshPage() => page?.Refresh();
    public void OpenBrowser(string relative = "/")
    {
        if (!Store.Preferences.BrowserSharingEnabled)
            Store.SetPreferences(Store.Preferences with
            {
                BrowserSharingEnabled = true
            });
        if (!Engine.Running && !Engine.Browser.Running)
            ToggleReceiving();
        else
            Engine.ApplySavedPreferences();
        if (!Engine.Browser.Running)
            throw new IOException("Browser sharing could not start. Check the selected network in Settings.");
        Process.Start(new ProcessStartInfo(new Uri(new Uri(Engine.Browser.Url), relative).AbsoluteUri) { UseShellExecute = true });
    }
    public void ToggleReceiving()
    {
        if (Engine.Running)
        {
            sharingStoppedManually = true;
            Engine.Stop();
        }
        else
        {
            NetworkAdapter network = NetworkAdapter.Available().FirstOrDefault() ?? throw new IOException("Connect this PC to a private IPv4 Wi-Fi or Ethernet network, then try again.");
            StartReceiving(network);
        }
        UpdateStatus();
        page?.Refresh();
    }
    public void StartReceiving(NetworkAdapter network)
    {
        if (Engine.Running)
            throw new InvalidOperationException("Stop receiving before changing the network adapter.");
        try
        {
            Engine.Start(network);
        }
        catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse) { Engine.Start(network, 0); }
        Tell("Ready to connect. Open Lantoss on the other PC and click its name. If Windows asks, allow Private networks only.");
        UpdateStatus();
        sharingStoppedManually = false;
        networkSignature = string.Join(";", NetworkAdapter.Available().Select(adapter => adapter.Address + "/" + adapter.Mask).Order());
        if (!preview)
            _ = Firewall.EnsureAsync();
    }
    private void RecoverNetwork()
    {
        if (disposed || closing || sharingStoppedManually || !Store.Preferences.StartReceivingOnOpen)
            return;
        try
        {
            NetworkAdapter[] available = NetworkAdapter.Available();
            string signature = string.Join(";", available.Select(adapter => adapter.Address + "/" + adapter.Mask).Order());
            if (Engine.Running && signature != networkSignature)
            {
                bool lost = !available.Any(adapter => adapter.Address.Equals(Engine.Adapter?.Address));
                if (!lost && Engine.Transfers.Records.Any(record => !record.Terminal))
                    return;
                Engine.Stop();
                UpdateStatus();
            }
            if (!Engine.Running && available.Length > 0)
            {
                StartReceiving(available[0]);
                Tell("Network reconnected. Discovery and linked-folder sync resumed automatically.");
            }
        }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or InvalidOperationException) { Tell("Waiting for home-network access. " + TransferEngine.HumanError(error)); }
    }
    /// <summary>Raised as soon as a PC or browser asks to send, before the approval window, so a hidden or minimized app is noticed.</summary>
    private void NotifyWaiting(BrowserDevice device)
    {
        if (notifications == null || !Store.Preferences.Notifications)
            return;
        notifications.BalloonTipTitle = device.Label + " wants to connect";
        notifications.BalloonTipText = device.Address + ". Open Lantoss and choose Allow in Phone & browser or Devices.";
        notifications.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifications.ShowBalloonTip(10000);
    }
    private void NotifyIncoming(ReceiveRequest request)
    {
        if (notifications == null || !Store.Preferences.Notifications)
            return;
        notifications.BalloonTipTitle = request.PeerName + " wants to send you files";
        notifications.BalloonTipText = $"{request.Manifest.Entries.Count} files · {Format.Bytes(request.Manifest.TotalBytes)}" + (request.Address.Length > 0 ? " · " + request.Address : "") + ". Click to review.";
        notifications.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifications.ShowBalloonTip(10000);
    }
    public void UpdateStatus()
    {
        Find<Button>("ReceiveToggle").Content = Engine.Running ? "Stop sharing" : "Start server";
        Find<TextBlock>("NetworkStatus").Text = Engine.Running ? "LAN · " + Engine.Endpoint : "Receiving off";
        Find<TextBlock>("ThisDevice").Text = Store.Preferences.DeviceName;
        Find<ContentControl>("ThisAvatar").Content = C.Avatar(Store.Preferences.DeviceName, Profiles.Picture(Store), 36);
        try
        {
            DriveInfo drive = new(Path.GetPathRoot(Store.Preferences.ReceiveFolder)!);
            Find<TextBlock>("StorageText").Text = $"{Format.Bytes(drive.AvailableFreeSpace)} free of {Format.Bytes(drive.TotalSize)}";
            Find<ProgressBar>("StorageBar").Value = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
        }
        catch (IOException) { Find<TextBlock>("StorageText").Text = "Receive drive unavailable"; Find<ProgressBar>("StorageBar").Value = 0; }
    }
    public void PickFiles()
    {
        OpenFileDialog picker = new()
        {
            Title = "Choose files to send",
            Multiselect = true,
            CheckFileExists = true
        };
        if (picker.ShowDialog(Window) == true)
            _ = AddSelectionsAsync(picker.FileNames);
    }
    public void PickFolders()
    {
        OpenFolderDialog picker = new()
        {
            Title = "Choose folders to send",
            Multiselect = true
        };
        if (picker.ShowDialog(Window) == true)
            _ = AddSelectionsAsync(picker.FolderNames);
    }
    public async Task AddSelectionsAsync(IEnumerable<string> paths)
    {
        if (selecting)
        {
            Tell("Wait for the current selection to finish scanning.");
            return;
        }
        selecting = true;
        try
        {
            string[] roots = Selection.Select(s => s.Path).Concat(paths).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (roots.Length > 256)
                throw new InvalidDataException("Select up to 256 top-level files or folders.");
            Tell("Checking selected files and folder contents…");
            Manifest manifest = await Task.Run(() => FileCatalog.BuildAsync(roots, Guid.NewGuid().ToString("N"), false, default));
            Selection.Clear();
            foreach (string root in roots)
            {
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                ManifestEntry[] entries = manifest.Entries.Where(e => e.SourcePath == root || e.SourcePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                Selection.Add(new(root, Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), entries.Sum(e => e.Size), entries.Count(e => !e.Directory), entries.Count(e => e.Directory)));
            }
            Navigate("Send");
            Tell($"{manifest.FileCount} files and {manifest.FolderCount} folders ready. Select a paired recipient.");
        }
        catch (Exception error) { Error(error); }
        finally { selecting = false; }
    }
    public void SendSelected(Peer peer)
    {
        _ = Engine.Send(peer, Selection.Select(s => s.Path));
        Navigate("Transfers");
        Tell("Transfer queued. The recipient must approve before any file bytes are sent.");
    }
    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) && !File.Exists(full))
            throw new FileNotFoundException("This location is no longer available.");
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { full } });
    }
    public void OpenInbox()
    {
        FileCatalog.CheckSafeDirectory(Store.Preferences.ReceiveFolder);
        Directory.CreateDirectory(Store.Preferences.ReceiveFolder);
        Reveal(Store.Preferences.ReceiveFolder);
    }
    public string EffectiveTheme { get; private set; } = "Light";
    public void SetAppearance(string mode)
    {
        // Update only this preference so switching appearance cannot reset network or sync options.
        Store.SetPreferences(Store.Preferences with
        {
            Theme = mode
        });
        ApplyTheme(mode);
    }
    public void ApplyTheme(string mode)
    {
        bool dark = Appearance.IsDark(mode, () => (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0);
        EffectiveTheme = dark ? "Dark" : "Light";
        // DynamicResource consumers update in place, preserving selections and active transfers.
        foreach ((string name, string color) in Appearance.Palette(dark))
            Window.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Find<ComboBox>("AppearanceSelector").SelectedItem = mode;
        Find<TextBlock>("AppearanceStatus").Text = mode == "System" ? "Following Windows · " + EffectiveTheme : EffectiveTheme + " appearance";
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Store.Preferences.Theme == "System")
            Ui(() => ApplyTheme("System"));
    }
    public void Render(string path, int width, int height)
    {
        FrameworkElement view = (FrameworkElement)Window.Content;
        view.SetResourceReference(Panel.BackgroundProperty, "Background");
        // Offscreen exports have no live window layout loop to invalidate old text sizes.
        static void InvalidateTree(DependencyObject node)
        {
            if (node is UIElement element)
            {
                element.InvalidateMeasure();
                element.InvalidateArrange();
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                InvalidateTree(VisualTreeHelper.GetChild(node, index));
        }
        InvalidateTree(view);
        view.Measure(new(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose || preview)
        {
            Dispose();
            return;
        }
        if (!exitRequested && Store.Preferences.CloseToTray)
        {
            e.Cancel = true;
            Window.Hide();
            if (!trayNoticeShown && notifications != null)
            {
                trayNoticeShown = true;
                notifications.BalloonTipTitle = "Lantoss is still running";
                notifications.BalloonTipText = "Receiving and sync continue in the tray. Use Exit Lantoss to stop the server.";
                notifications.ShowBalloonTip(4000);
            }
            return;
        }
        if (closing)
        {
            e.Cancel = true;
            return;
        }
        if (!preview && Engine.Transfers.Records.Any(r => !r.Terminal) && MessageBox.Show(Window, "Stop active transfers and close Lantoss? Files already verified and saved will stay on disk.", "Close Lantoss", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        e.Cancel = true;
        closing = true;
        Window.IsEnabled = false;
        Tell("Closing connections and saving transfer history…");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try
        {
            await Engine.ShutdownAsync(timeout.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
        finally { allowClose = true; Dispose(); Window.Close(); }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        networkTimer.Stop();
        if (!preview)
            SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        Engine.Dispose();
        notifications?.Dispose();
        trayIcon?.Dispose();
        identity.Dispose();
    }
}
