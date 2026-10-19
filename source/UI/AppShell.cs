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
    private readonly Identity identity;
    private readonly bool preview;
    public bool Preview => preview;
    private readonly Dictionary<string, Button> navigation = [];
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
        Engine.ApproveReceive = Dialogs.ReceiveAsync;
        Engine.ApproveSync = Dialogs.SyncAsync;
        Engine.SyncChanged += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Sync.Changed += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Status += message => Ui(() => Tell(message));
        Engine.PeersChanged += () => Ui(() => { if (CurrentPage is "Home" or "Devices" or "Send" or "Shared Spaces") page?.Refresh(); });
        Engine.Browser.Changed += () => Ui(() => { if (CurrentPage is "Phone & browser" or "Shared Spaces") page?.Refresh(); if (!preview && Engine.Running) _ = Firewall.EnsureAsync(); });
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
            notifications.DoubleClick += (_, _) => { Window.WindowState = WindowState.Normal; Window.Show(); Window.Activate(); };
        }
        string[] names = ["Home", "Send", "Transfers", "Devices", "Phone & browser", "Shared Spaces", "Sync", "History", "Settings"];
        string[] icons = ["home", "send", "transfer", "devices", "devices", "folder", "sync", "history", "settings"];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            FrameworkElement glyph = C.Icon(icons[i], 18);
            glyph.Margin = new(0, 0, 14, 0);
            Button button = C.Button(name, () => Navigate(name));
            button.Content = C.Inline(glyph, C.Text(name, 12));
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
    public void Navigate(string destination)
    {
        CurrentPage = destination;
        page = null;
        Find<TextBox>("SearchBox").Text = "";
        Find<TextBlock>("PageTitle").Text = destination;
        Find<TextBox>("SearchBox").IsEnabled = destination is "Home" or "Send" or "Transfers" or "Devices" or "History" or "Shared Spaces";
        foreach ((string name, Button button) in navigation)
            button.SetResourceReference(Control.BackgroundProperty, name == destination ? "PrimarySoft" : "Sidebar");
        page = destination switch
        {
            "Home" => new HomePage(this),
            "Send" => new SendPage(this),
            "Transfers" => new TransfersPage(this),
            "Devices" => new DevicesPage(this),
            "Phone & browser" => new PhoneBrowserPage(this),
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
    public void UpdateStatus()
    {
        Find<Button>("ReceiveToggle").Content = Engine.Running ? "Stop sharing" : "Start receiving";
        Find<TextBlock>("NetworkStatus").Text = Engine.Running ? "LAN · " + Engine.Endpoint : "Receiving off";
        Find<TextBlock>("ThisDevice").Text = Store.Preferences.DeviceName;
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
