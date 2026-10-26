using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Lantoss.Desktop;

internal interface IPage
{
    Control View
    {
        get;
    }
    void Refresh();
}

/// <summary>
/// Owns the window, navigation and engine lifetime on macOS and Linux. Engine events arrive off-thread:
/// marshal UI work through Ui rather than touching controls directly.
/// </summary>
internal sealed class Shell : IDisposable
{
    public static readonly string[] Pages = ["Home", "Send", "Transfers", "Devices", "Phone & browser", "Text & links", "Shared Spaces", "Sync", "History", "Settings"];
    private static readonly string[] Icons = ["home", "send", "transfer", "devices", "phone", "file", "folder", "sync", "history", "settings"];
    public static Shell Current { get; private set; } = null!;
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
    public Dialogs Dialogs
    {
        get;
    }
    public bool Preview
    {
        get;
    }
    public List<SelectionItem> Selection { get; } = [];
    public string? SelectedPeerKey
    {
        get; set;
    }
    public string CurrentPage { get; private set; } = "Home";
    public string Search => search.Text?.Trim() ?? "";
    public static string Platform => OperatingSystem.IsMacOS() ? "Mac" : OperatingSystem.IsLinux() ? "Linux PC" : "PC";
    private readonly Dictionary<string, Button> navigation = [];
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private TextBox search = new();
    private TextBlock feedback = new(), networkStatus = new(), pageTitle = new(), storageText = new();
    private ProgressBar storageBar = new();
    private ContentControl thisDevice = new(), content = new();
    private ScrollViewer scroll = new();
    private Button receiveToggle = new();
    private IPage? page;
    private TrayIcon? tray;
    private bool exitRequested, closing, allowClose, disposed, selecting, sharingStoppedManually, trayNoticeShown;
    private string networkSignature = "";
    public Shell(MetadataStore store, Identity identity, bool preview)
    {
        Current = this;
        Store = store;
        Preview = preview;
        Engine = new(store, identity, allowLoopback: preview);
        Dialogs = new(this);
        Window = new()
        {
            Title = "Lantoss",
            Width = 1280,
            Height = 840,
            MinWidth = 900,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Icon = AppIcon() is { } icon ? new WindowIcon(icon) : null
        };
        Engine.ApprovePair = Dialogs.CompareAsync;
        Engine.ApproveReceive = (request, token) => { Ui(() => NotifyIncoming(request)); return Dialogs.ReceiveAsync(request, token); };
        Engine.ApproveSync = Dialogs.SyncAsync;
        Engine.SyncChanged += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Sync.Changed += () => Ui(() => { if (CurrentPage == "Sync") page?.Refresh(); });
        Engine.Browser.Notes.Changed += () => Ui(() => { if (CurrentPage == "Text & links") page?.Refresh(); });
        Engine.Status += message => Ui(() => Tell(message));
        Engine.PeersChanged += () => Ui(() => { if (CurrentPage is "Home" or "Devices" or "Send" or "Shared Spaces" or "Text & links") page?.Refresh(); });
        Engine.Browser.Changed += () => Ui(() => { if (CurrentPage is "Phone & browser" or "Shared Spaces" or "Devices" or "Home") page?.Refresh(); });
        Engine.Transfers.Changed += _ => Ui(() => { if (CurrentPage is "Home" or "Transfers" or "History") page?.Refresh(); });
        Engine.Transfers.PersistenceFailed += _ => Ui(() => Tell("History could not be saved. Check free space and permissions in the app's data folder."));
        Engine.Completed += record => Ui(() =>
        {
            if (Store.Preferences.Notifications)
                Notify(record.Direction == "Received" ? "Files received" : "Transfer finished", $"{record.FileCount} files · {record.PeerName}");
        });
        Window.Closing += OnClosing;
        networkTimer.Tick += (_, _) => RecoverNetwork();
        Window.Opened += (_, _) =>
        {
            if (preview)
                return;
            networkTimer.Start();
            if (!Store.Preferences.StartReceivingOnOpen || Engine.Running)
                return;
            try
            {
                ToggleReceiving();
            }
            catch (Exception error) { Tell("Nearby connection could not start. " + TransferEngine.HumanError(error)); }
        };
        DragDrop.SetAllowDrop(Window, true);
        Window.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            string[] paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            if (paths.Length > 0)
                await AddSelectionsAsync(paths);
        });
        if (Application.Current?.PlatformSettings is { } settings)
            settings.ColorValuesChanged += (_, _) => { if (Store.Preferences.Theme == "System") Ui(() => ApplyTheme("System")); };
        if (!preview)
            CreateTray();
        ApplyTheme(store.Preferences.Theme);
        if (store.RecoveryNotice != null)
            Tell(store.RecoveryNotice);
    }
    public static Stream? AppIcon()
    {
        Uri uri = new("avares://Lantoss/Assets/lantoss-256.png");
        return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
    }
    public void Ui(Action action)
    {
        if (!disposed)
            Dispatcher.UIThread.Post(action);
    }
    public void Tell(string message) => feedback.Text = message;
    public void Error(Exception error)
    {
        Tell(TransferEngine.HumanError(error));
        if (!Preview)
            _ = Dialogs.MessageAsync("Lantoss", TransferEngine.HumanError(error));
    }
    public void ShowWindow()
    {
        Window.Show();
        if (Window.WindowState == WindowState.Minimized)
            Window.WindowState = WindowState.Normal;
        Window.Activate();
    }
    /// <summary>Appearance changes rebuild the shell with the new palette, keeping the current page, selection and engine.</summary>
    public void ApplyTheme(string mode)
    {
        Theme.Apply(mode);
        string message = feedback.Text ?? "Ready to share on your local network.";
        Build(mode);
        Tell(message);
        Navigate(CurrentPage);
        UpdateStatus();
    }
    private void Build(string mode)
    {
        Window.Background = Theme.Brush("Background");
        navigation.Clear();
        StackPanel nav = new()
        {
            Spacing = 4
        };
        for (int index = 0; index < Pages.Length; index++)
        {
            string name = Pages[index];
            Button button = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new(12, 9),
                CornerRadius = new(10),
                BorderThickness = new(0)
            };
            button.Click += (_, _) => Navigate(name);
            button.Tag = Icons[index];
            navigation[name] = button;
            nav.Children.Add(button);
        }
        ComboBox appearance = new()
        {
            ItemsSource = Appearance.Modes,
            SelectedItem = mode,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(0, 6, 0, 6)
        };
        appearance.SelectionChanged += (_, _) =>
        {
            if (appearance.SelectedItem is string selected && selected != Store.Preferences.Theme)
            {
                // Only the appearance preference changes, so switching cannot reset network or sync options.
                Store.SetPreferences(Store.Preferences with
                {
                    Theme = selected
                });
                Dispatcher.UIThread.Post(() => ApplyTheme(selected));
            }
        };
        Control logo = AppIcon() is { } stream ? new Image { Source = new Bitmap(stream), Width = 36, Height = 36 } : C.Icon("transfer", 30);
        logo.Margin = new(0, 0, 12, 0);
        TextBlock brand = C.Text("Lantoss", 21, bold: true);
        brand.VerticalAlignment = VerticalAlignment.Center;
        StackPanel top = C.Stack(C.Space(C.Inline(logo, brand), 22), C.Text("APPEARANCE", 10, "Muted", true), appearance,
            C.Space(C.Text(mode == "System" ? "Following your system · " + (Theme.Dark ? "Dark" : "Light") : (Theme.Dark ? "Dark" : "Light") + " appearance", 11, "Muted"), 18), nav);
        thisDevice = new()
        {
            Focusable = false
        };
        storageText = C.Text("", 11, "Muted");
        storageBar = new()
        {
            Minimum = 0,
            Maximum = 100,
            Height = 5,
            MinHeight = 5,
            Foreground = Theme.Brush("Primary"),
            Background = Theme.Brush("Border"),
            Margin = new(4, 6, 4, 18)
        };
        StackPanel bottom = C.Stack(C.Space(new Border { Child = thisDevice, Padding = new(12), CornerRadius = new(12), Background = Theme.Brush("PrimarySoft") }, 22), C.Text("LOCAL STORAGE", 10, "Muted", true), storageText, storageBar,
            C.Text("● No account. No cloud.", 11, "Muted"), C.Space(C.Text("Lantoss  /  2.0  ·  " + (OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Desktop"), 9, "Muted"), 0, 10));
        DockPanel sideLayout = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        sideLayout.Children.Add(bottom);
        sideLayout.Children.Add(top);
        Border sidebar = new()
        {
            Background = Theme.Brush("Sidebar"),
            BorderBrush = Theme.Brush("Border"),
            BorderThickness = new(0, 0, 1, 0),
            Child = new ScrollViewer { Content = new Border { Padding = new(18, 24, 18, 18), Child = sideLayout }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        search = new()
        {
            PlaceholderText = "Search this view…",
            Width = 210,
            VerticalAlignment = VerticalAlignment.Center
        };
        search.TextChanged += (_, _) => page?.Refresh();
        Button browser = C.Button("Open in browser", () => OpenBrowser(), icon: "phone");
        receiveToggle = C.Button("Start server", ToggleReceiving, true);
        StackPanel actions = C.Inline(search, browser, receiveToggle);
        actions.Spacing = 10;
        actions.VerticalAlignment = VerticalAlignment.Center;
        pageTitle = C.Text(CurrentPage, 21, bold: true);
        pageTitle.VerticalAlignment = VerticalAlignment.Center;
        DockPanel headerLayout = new();
        DockPanel.SetDock(actions, Dock.Right);
        headerLayout.Children.Add(actions);
        headerLayout.Children.Add(pageTitle);
        Border header = new()
        {
            Background = Theme.Brush("Surface"),
            BorderBrush = Theme.Brush("Border"),
            BorderThickness = new(0, 0, 0, 1),
            Padding = new(32, 0),
            Child = headerLayout
        };
        content = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        scroll = new()
        {
            Content = new Border { Padding = new(32, 28), Child = content },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        feedback = C.Text("Ready to share on your local network.", 11, "Muted");
        feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        feedback.TextWrapping = TextWrapping.NoWrap;
        networkStatus = C.Text("Receiving off", 11, "Muted");
        DockPanel statusLayout = new();
        DockPanel.SetDock(networkStatus, Dock.Right);
        statusLayout.Children.Add(networkStatus);
        statusLayout.Children.Add(feedback);
        Border status = new()
        {
            Background = Theme.Brush("Surface"),
            BorderBrush = Theme.Brush("Border"),
            BorderThickness = new(0, 1, 0, 0),
            Padding = new(32, 8),
            Child = statusLayout
        };
        Grid main = new()
        {
            RowDefinitions = new RowDefinitions("76,*,Auto")
        };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(status, 2);
        main.Children.Add(header);
        main.Children.Add(scroll);
        main.Children.Add(status);
        Grid root = new()
        {
            ColumnDefinitions = new ColumnDefinitions("236,*")
        };
        Grid.SetColumn(main, 1);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        Window.Content = root;
    }
    public void Navigate(string destination)
    {
        CurrentPage = destination;
        search.Text = "";
        pageTitle.Text = destination;
        foreach ((string name, Button button) in navigation)
        {
            bool active = name == destination;
            Theme.SetButton(button.Resources, active ? "Accent" : "Sidebar", active ? "OnAccent" : "Text", active ? "Accent" : "Sidebar", active ? "Primary" : "PrimarySoft");
            Control glyph = C.Icon((string)button.Tag!, 18, active ? "OnAccent" : "Primary");
            glyph.Margin = new(0, 0, 6, 0);
            TextBlock label = C.Text(name, 13, active ? "OnAccent" : "Text", active);
            label.VerticalAlignment = VerticalAlignment.Center;
            button.Content = C.Inline(glyph, label);
        }
        page = destination switch
        {
            "Home" => new HomePage(this),
            "Send" => new SendPage(this),
            "Transfers" => new TransfersPage(this),
            "Devices" => new DevicesPage(this),
            "Phone & browser" => new PhonePage(this),
            "Text & links" => new NotesPage(this),
            "Shared Spaces" => new SpacesPage(this),
            "Sync" => new SyncPage(this),
            "History" => new HistoryPage(this),
            _ => new SettingsPage(this)
        };
        content.Content = page.View;
        scroll.Offset = default;
        page.Refresh();
    }
    public void RefreshPage() => page?.Refresh();
    public void UpdateStatus()
    {
        receiveToggle.Content = Engine.Running ? "Stop sharing" : "Start server";
        networkStatus.Text = Engine.Running ? "LAN · " + Engine.Endpoint : "Receiving off";
        Control avatar = C.Avatar(Store.Preferences.DeviceName, Profiles.Picture(Store), 36);
        avatar.Margin = new(0, 0, 10, 0);
        TextBlock name = C.Text(Store.Preferences.DeviceName, 12, bold: true);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel card = new();
        DockPanel.SetDock(avatar, Dock.Left);
        card.Children.Add(avatar);
        StackPanel text = C.Stack(name, C.Space(C.Text("This " + Platform, 11, "Muted"), 0, 4));
        text.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(text);
        thisDevice.Content = card;
        try
        {
            DriveInfo drive = new(Path.GetPathRoot(Path.GetFullPath(Store.Preferences.ReceiveFolder))!);
            storageText.Text = $"{Format.Bytes(drive.AvailableFreeSpace)} free of {Format.Bytes(drive.TotalSize)}";
            storageBar.Value = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / Math.Max(1, drive.TotalSize);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { storageText.Text = "Receive drive unavailable"; storageBar.Value = 0; }
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
            NetworkAdapter network = NetworkAdapter.Available().FirstOrDefault() ?? throw new IOException("Connect this " + Platform + " to a private IPv4 Wi-Fi or Ethernet network, then try again.");
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
        catch (System.Net.Sockets.SocketException error) when (error.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse) { Engine.Start(network, 0); }
        Tell("Ready to connect. Open Lantoss on the other device and click its name." + (OperatingSystem.IsMacOS() ? " If macOS asks, allow incoming connections and local network access." : ""));
        UpdateStatus();
        sharingStoppedManually = false;
        networkSignature = string.Join(";", NetworkAdapter.Available().Select(adapter => adapter.Address + "/" + adapter.Mask).Order());
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
        Open(new Uri(new Uri(Engine.Browser.Url), relative).AbsoluteUri);
    }
    /// <summary>Opens a URL, file or folder with the system handler (open on macOS, xdg-open on Linux).</summary>
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) && !File.Exists(full))
            throw new FileNotFoundException("This location is no longer available.");
        Open(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!);
    }
    public void OpenInbox()
    {
        FileCatalog.CheckSafeDirectory(Store.Preferences.ReceiveFolder);
        Directory.CreateDirectory(Store.Preferences.ReceiveFolder);
        Reveal(Store.Preferences.ReceiveFolder);
    }
    public async Task CopyAsync(string text, string message)
    {
        if (TopLevel.GetTopLevel(Window)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
        Tell(message);
    }
    public async Task<string[]> PickFilesAsync(string title, bool multiple = true, IReadOnlyList<FilePickerFileType>? filter = null)
    {
        IReadOnlyList<IStorageFile> files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = multiple, FileTypeFilter = filter });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    }
    public async Task<string[]> PickFoldersAsync(string title, bool multiple = false)
    {
        IReadOnlyList<IStorageFolder> folders = await Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = multiple });
        return folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();
    }
    public async Task PickFilesAsync() => await AddSelectionsAsync(await PickFilesAsync("Choose files to send"));
    public async Task PickFoldersAsync() => await AddSelectionsAsync(await PickFoldersAsync("Choose folders to send", true));
    public async Task AddSelectionsAsync(IEnumerable<string> paths)
    {
        string[] added = paths.ToArray();
        if (added.Length == 0)
            return;
        if (selecting)
        {
            Tell("Wait for the current selection to finish scanning.");
            return;
        }
        selecting = true;
        try
        {
            StringComparer comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            string[] roots = Selection.Select(item => item.Path).Concat(added).Select(Path.GetFullPath).Distinct(comparer).ToArray();
            if (roots.Length > 256)
                throw new InvalidDataException("Select up to 256 top-level files or folders.");
            Tell("Checking selected files and folder contents…");
            Manifest manifest = await Task.Run(() => FileCatalog.BuildAsync(roots, Guid.NewGuid().ToString("N"), false, default));
            Selection.Clear();
            foreach (string root in roots)
            {
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                ManifestEntry[] entries = manifest.Entries.Where(entry => entry.SourcePath == root || entry.SourcePath.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                Selection.Add(new(root, Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), entries.Sum(entry => entry.Size), entries.Count(entry => !entry.Directory), entries.Count(entry => entry.Directory)));
            }
            Navigate("Send");
            Tell($"{manifest.FileCount} files and {manifest.FolderCount} folders ready. Select a paired recipient.");
        }
        catch (Exception error) { Error(error); }
        finally { selecting = false; }
    }
    public void SendSelected(Peer peer)
    {
        _ = Engine.Send(peer, Selection.Select(item => item.Path));
        Navigate("Transfers");
        Tell("Transfer queued. The recipient must approve before any file bytes are sent.");
    }
    /// <summary>Native notification: osascript on macOS and notify-send on Linux. Text is passed as arguments, never as script source.</summary>
    public void Notify(string title, string body)
    {
        Tell(title + " · " + body);
        if (Preview)
            return;
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsMacOS())
            {
                info = new("osascript");
                foreach (string argument in new[] { "-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", title, body })
                    info.ArgumentList.Add(argument);
            }
            else if (OperatingSystem.IsLinux())
            {
                info = new("notify-send");
                foreach (string argument in new[] { "--app-name=Lantoss", title, body })
                    info.ArgumentList.Add(argument);
            }
            else
                return;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process.Start(info)?.Dispose();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException) { }
    }
    /// <summary>Raised as soon as a PC or browser asks to send, before the approval window, so a minimized app is noticed.</summary>
    private void NotifyIncoming(ReceiveRequest request)
    {
        if (Store.Preferences.Notifications)
            Notify(request.PeerName + " wants to send you files", $"{request.Manifest.Entries.Count} files · {Format.Bytes(request.Manifest.TotalBytes)}" + (request.Address.Length > 0 ? " · " + request.Address : ""));
    }
    private void CreateTray()
    {
        try
        {
            NativeMenu menu = new();
            NativeMenuItem open = new("Open Lantoss"), start = new("Start sharing server"), stop = new("Stop sharing server"), exit = new("Exit Lantoss");
            open.Click += (_, _) => Ui(ShowWindow);
            start.Click += (_, _) => Ui(() => { try { if (!Engine.Running) ToggleReceiving(); } catch (Exception error) { Error(error); } });
            stop.Click += (_, _) => Ui(() => { if (Engine.Running) ToggleReceiving(); });
            exit.Click += (_, _) => Ui(Exit);
            menu.Items.Add(open);
            menu.Items.Add(start);
            menu.Items.Add(stop);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exit);
            tray = new()
            {
                Icon = Window.Icon,
                ToolTipText = "Lantoss — local file sharing",
                Menu = menu,
                IsVisible = true
            };
            tray.Clicked += (_, _) => Ui(ShowWindow);
            TrayIcon.SetIcons(Application.Current!, [tray]);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or PlatformNotSupportedException) { tray = null; }
    }
    public void Exit()
    {
        exitRequested = true;
        Window.Close();
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose || Preview)
        {
            Dispose();
            return;
        }
        e.Cancel = true;
        if (!exitRequested && Store.Preferences.CloseToTray)
        {
            // Minimize rather than hide: not every Linux desktop shows tray icons, and a hidden window could not be reopened.
            Window.WindowState = WindowState.Minimized;
            if (!trayNoticeShown)
            {
                trayNoticeShown = true;
                Notify("Lantoss is still running", "Receiving and sync continue. Use Exit Lantoss in the tray menu, or turn off keep-running in Settings.");
            }
            return;
        }
        exitRequested = false;
        if (closing)
            return;
        if (Engine.Transfers.Records.Any(record => !record.Terminal) && !await Dialogs.ConfirmAsync("Close Lantoss", "Stop active transfers and close Lantoss? Files already verified and saved stay on disk."))
            return;
        closing = true;
        Window.IsEnabled = false;
        Tell("Closing connections and saving transfer history…");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try
        {
            await Engine.ShutdownAsync(timeout.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
        finally
        {
            allowClose = true;
            Dispose();
            Window.Close();
        }
    }
    /// <summary>Quit from the system (Cmd+Q, log out): close for real, still draining transfers and history.</summary>
    public bool RequestShutdown()
    {
        if (allowClose)
            return false;
        Exit();
        return true;
    }
    public async Task RenderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string mode in new[] { "Light", "Dark" })
        {
            ApplyTheme(mode);
            foreach (string name in Pages)
            {
                Navigate(name);
                await Task.Delay(250);
                Size size = Window.ClientSize;
                using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(new PixelSize((int)size.Width, (int)size.Height));
                bitmap.Render(Window);
                bitmap.Save(Path.Combine(folder, $"{mode}-{name.Replace(' ', '-').Replace("&", "and")}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        networkTimer.Stop();
        tray?.Dispose();
        Engine.Dispose();
    }
}
