using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
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
    // Menu rows: page, title, subtitle (Share group only) and icon.
    private static readonly (string Page, string Title, string Detail, string Icon)[] ShareMenu = [("Send", "Send files", "To a PC on your network", "send"), ("Shared Spaces", "Share a folder", "Shared spaces for your PCs", "folder"), ("Text & links", "Share text or a link", "Notes, addresses, URLs", "text"), ("Phone & browser", "Phone & browser", "Scan a QR code, no app needed", "phone")];
    private static readonly (string Page, string Icon)[] ManageMenu = [("Transfers", "transfer"), ("Devices", "devices"), ("Sync", "sync"), ("History", "history"), ("Settings", "settings")];
    private static readonly string[] Searchable = ["Send", "Transfers", "Devices", "Text & links", "Shared Spaces", "History"];
    /// <summary>The name shown in the breadcrumb; page keys stay stable for navigation and renders.</summary>
    public static string Title(string page) => page switch
    {
        "Send" => "Send files",
        "Shared Spaces" => "Shared folders",
        _ => page
    };
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
    /// <summary>A phone or browser chosen on Home or Send, preselected as the Send recipient.</summary>
    public string? SelectedBrowserHandle
    {
        get; set;
    }
    /// <summary>A PC chosen with Text on Home, preselected once on Text &amp; links.</summary>
    public string? NotePeerKey
    {
        get; set;
    }
    public string CurrentPage { get; private set; } = "Home";
    public string Search => search.Text?.Trim() ?? "";
    public static string Platform => OperatingSystem.IsMacOS() ? "Mac" : OperatingSystem.IsLinux() ? "Linux PC" : "PC";
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private TextBox search = new();
    private TextBlock feedback = new(), networkStatus = new(), pageTitle = new(), slash = new(), storageText = new();
    private ProgressBar storageBar = new();
    private ContentControl thisDevice = new(), content = new();
    private ScrollViewer scroll = new();
    private Button receiveToggle = new();
    private Ellipse networkDot = new();
    private Border header = new(), body = new(), menuPanel = new(), scrim = new();
    private Panel menu = new();
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
            Title = "hopo",
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
        Engine.Browser.ApprovalRequested += device => Ui(() => { string text = device.Label + " wants to connect. Open hopo to allow it."; Tell(text); if (Store.Preferences.Notifications) Notify("New phone or browser", text); });
        Engine.Transfers.Changed += _ => Ui(() => { if (CurrentPage is "Home" or "Transfers" or "History") page?.Refresh(); });
        Engine.Transfers.PersistenceFailed += _ => Ui(() => Tell("History could not be saved. Check free space and permissions in the app's data folder."));
        Engine.Completed += record => Ui(() =>
        {
            if (Store.Preferences.Notifications)
                Notify(record.Direction == "Received" ? "Files received" : "Transfer finished", $"{record.FileCount} files · {record.PeerName}");
        });
        Window.Closing += OnClosing;
        Window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && menu.IsVisible)
            {
                CloseMenu();
                e.Handled = true;
            }
        };
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
        Motion.Setup(preview);
        ApplyTheme(store.Preferences.Theme);
        if (store.RecoveryNotice != null)
            Tell(store.RecoveryNotice);
    }
    public static Stream? AppIcon()
    {
        Uri uri = new("avares://hopo/Assets/lantoss-256.png");
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
            _ = Dialogs.MessageAsync("hopo", TransferEngine.HumanError(error));
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
        // The hopo field behind every page: a purple glow top-left, a green glow bottom-right and faint rings centred on the window.
        Panel field = new()
        {
            IsHitTestVisible = false,
            ClipToBounds = true
        };
        // Each glow is the design's 1.25 W × 1.35 H ellipse (purple at −.40 W, −.55 H; green mirrored bottom-right), drawn by a window-relative brush so it follows every resize.
        // They drift slowly: purple to (-14%, 10%) at 95% over 60 s, green to (18%, 12%) at 110% over 68 s, as shares of the glow.
        RadialGradientBrush violet = (RadialGradientBrush)Theme.Glow(Theme.Dark ? "#8C3CFF" : "#8F5BFF", Theme.Dark ? 0.52 : 0.42, 0.225, 0.125, 0.625, 0.675), green = (RadialGradientBrush)Theme.Glow(Theme.Dark ? "#28F096" : "#24C98A", Theme.Dark ? 0.4 : 0.36, 0.775, 0.875, 0.625, 0.675);
        field.Children.Add(new Border { Background = violet });
        field.Children.Add(new Border { Background = green });
        Motion.Drift(field, violet, -0.175, 0.135, 0.95, 60);
        Motion.Drift(field, green, 0.225, 0.162, 1.1, 68);
        field.Children.Add(new Border { Child = C.Rings(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        TextBlock brand = C.Text("hopo", 17, bold: true);
        brand.LetterSpacing = -0.2;
        brand.VerticalAlignment = VerticalAlignment.Center;
        StackPanel mark = C.Inline(C.Mark(), brand);
        mark.Spacing = 10;
        Button home = C.Ghost(new Button { Content = mark, Padding = new(4, 2), BorderThickness = new(1), CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Center }, "Text");
        home.Click += (_, _) => Navigate("Home");
        Avalonia.Automation.AutomationProperties.SetName(home, "hopo home");
        slash = C.Text("/", 14, "Muted/55");
        pageTitle = C.Text(Title(CurrentPage), 14, "Muted");
        slash.VerticalAlignment = pageTitle.VerticalAlignment = VerticalAlignment.Center;
        StackPanel crumbs = C.Inline(home, slash, pageTitle);
        crumbs.Spacing = 12;
        search = new()
        {
            PlaceholderText = "Search this view…",
            Width = 200,
            MinHeight = 40,
            CornerRadius = new(12),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        search.TextChanged += (_, _) => page?.Refresh();
        networkDot = C.Dot("Mint", 7);
        networkStatus = C.Text("Receiving off", 12, "Text/85");
        networkStatus.FontWeight = FontWeight.Medium;
        networkStatus.TextWrapping = TextWrapping.NoWrap;
        networkStatus.VerticalAlignment = VerticalAlignment.Center;
        StackPanel chipRow = C.Inline(networkDot, networkStatus);
        chipRow.Spacing = 7;
        Border chip = new()
        {
            Child = chipRow,
            Height = 32,
            Padding = new(12, 0),
            CornerRadius = new(10),
            Background = Theme.Brush("Text/6"),
            VerticalAlignment = VerticalAlignment.Center
        };
        receiveToggle = C.Button("Start receiving", ToggleReceiving);
        StackPanel actions = C.Inline(search, chip, receiveToggle, C.IconButton("menu", OpenMenu, "Open menu"));
        actions.Spacing = 10;
        actions.VerticalAlignment = VerticalAlignment.Center;
        DockPanel headerLayout = new();
        DockPanel.SetDock(actions, Dock.Right);
        headerLayout.Children.Add(actions);
        headerLayout.Children.Add(crumbs);
        header = new()
        {
            Padding = new(32, 18),
            BorderBrush = Theme.Brush("Text/6"),
            Child = headerLayout
        };
        content = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        scroll = new()
        {
            Content = new Border { MaxWidth = 1120, Padding = new(32, 48, 32, 64), Child = content },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        body = new();
        feedback = C.Text("Ready to share on your local network.", 11, "Muted");
        feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        feedback.TextWrapping = TextWrapping.NoWrap;
        Border status = new()
        {
            BorderBrush = Theme.Brush("Text/6"),
            BorderThickness = new(0, 1, 0, 0),
            Padding = new(32, 7),
            Child = feedback
        };
        Grid main = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(status, 2);
        main.Children.Add(header);
        main.Children.Add(body);
        main.Children.Add(status);
        // The menu slides over a scrim from the right; the scrim, the close button and Escape dismiss it.
        menuPanel = new()
        {
            Width = 380,
            Margin = new(18, 4, 18, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new(24),
            Padding = new(14),
            Background = Theme.Brush("Menu"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            BoxShadow = Theme.Shadow(30, 80, 0.6)
        };
        scrim = new()
        {
            Background = Theme.Brush("Scrim")
        };
        scrim.PointerPressed += (_, _) => CloseMenu();
        menu = new Panel { IsVisible = false, Children = { scrim, menuPanel } };
        Window.Content = new Panel { Children = { field, main, menu } };
    }
    public void CloseMenu() => menu.IsVisible = false;
    public void OpenMenu()
    {
        TextBlock title = C.Text("Menu", 15, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new(10, 0, 0, 0);
        Button close = C.IconButton("close", CloseMenu, "Close menu");
        DockPanel top = new()
        {
            Margin = new(0, 2, 0, 10)
        };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(title);
        TextBlock share = C.Eyebrow("Share");
        share.Margin = new(10, 6);
        StackPanel items = C.Stack(top, share);
        items.Spacing = 2;
        foreach ((string target, string name, string detail, string icon) in ShareMenu)
            items.Children.Add(MenuRow(target, name, detail, icon, true));
        items.Children.Add(new Border { Height = 1, Background = Theme.Brush("Text/8"), Margin = new(10, 8) });
        TextBlock manage = C.Eyebrow("Manage");
        manage.Margin = new(10, 6);
        items.Children.Add(manage);
        int active = Engine.Transfers.Records.Count(record => !record.Terminal);
        foreach ((string target, string icon) in ManageMenu)
            items.Children.Add(MenuRow(target, target, target == "Transfers" && active > 0 ? active + " active" : "", icon, false));
        thisDevice = new()
        {
            Focusable = false
        };
        storageText = C.Text("", 12, "Muted");
        storageText.TextWrapping = TextWrapping.NoWrap;
        storageBar = new()
        {
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            MinHeight = 4,
            CornerRadius = new(2),
            Foreground = Theme.Brush("Accent"),
            Background = Theme.Brush("Text/8")
        };
        DockPanel storage = new();
        DockPanel.SetDock(storageText, Dock.Right);
        storage.Children.Add(storageText);
        storage.Children.Add(C.Text("Local storage", 12, "Muted"));
        TextBlock appearance = C.Eyebrow("Appearance");
        appearance.VerticalAlignment = VerticalAlignment.Center;
        appearance.Width = 104;
        DockPanel theme = new();
        DockPanel.SetDock(appearance, Dock.Left);
        theme.Children.Add(appearance);
        theme.Children.Add(AppearancePicker(true));
        StackPanel bottom = C.Stack(C.Space(new Border { Child = thisDevice, Padding = new(12), CornerRadius = new(14), Background = Theme.Brush("Text/4"), BorderBrush = Theme.Brush("Text/6"), BorderThickness = new(1) }, 14, 12),
            C.Space(storage, 8), C.Space(storageBar, 10), C.Space(C.Text("No account. No cloud. · hopo 2.0", 12, "Muted"), 14), theme);
        bottom.Margin = new(4, 0, 4, 2);
        // The bottom block sits at the foot of the panel when it fits and scrolls with the rows when the window is short.
        Grid layout = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            MinHeight = Math.Max(0, Window.ClientSize.Height - 28 - 30)
        };
        Grid.SetRow(bottom, 1);
        layout.Children.Add(items);
        layout.Children.Add(bottom);
        menuPanel.Child = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        UpdateStatus();
        menu.IsVisible = true;
        // The scrim fades in, the panel grows from the menu button and the rows fade up one after another.
        menuPanel.RenderTransformOrigin = new RelativePoint(menuPanel.Width - 36, 38, RelativeUnit.Absolute);
        Motion.Fade(scrim, 0.4);
        Motion.Open(menuPanel);
        Button[] rows = items.Children.OfType<Button>().Where(button => button.Classes.Contains("row")).ToArray();
        for (int index = 0; index < rows.Length; index++)
            Motion.Rise(rows[index], 6, 0.5, 0.12 + index * 0.04, Motion.Grow);
    }
    private Button MenuRow(string target, string title, string detail, string icon, bool share)
    {
        Border tile = C.Tile(icon, share ? "Primary" : "Text", 36, share ? "Accent" : null);
        tile.Margin = new(0, 0, 14, 0);
        tile.Classes.Add("tile");
        DockPanel row = new();
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        if (!share && detail.Length > 0)
        {
            TextBlock count = C.Text(detail, 11, "Mint", true);
            count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
        }
        Control text = share ? C.Stack(C.Medium(title, 13), C.Space(C.Text(detail, 11, "Muted"), 0, 1)) : C.Medium(title, 13);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        Button button = C.Ghost(new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Padding = new(10, share ? 4 : 3), MinHeight = share ? 50 : 44, CornerRadius = new(14), BorderThickness = new(1) }, "Text");
        button.Classes.Add("row");
        if (target == CurrentPage)
            Theme.SetButton(button.Resources, "Text/6", "Text", C.Clear, "Text/8");
        button.Click += (_, _) => Navigate(target);
        return button;
    }
    /// <summary>System, Light and Dark as one segmented control. Only the appearance preference changes, so switching cannot reset network or sync options.</summary>
    public Control AppearancePicker(bool reopenMenu = false)
    {
        string mode = Store.Preferences.Theme;
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*")
        };
        for (int index = 0; index < Appearance.Modes.Length; index++)
        {
            string option = Appearance.Modes[index];
            Button button = C.Small(option, () =>
            {
                if (option == Store.Preferences.Theme)
                    return;
                Store.SetPreferences(Store.Preferences with
                {
                    Theme = option
                });
                Dispatcher.UIThread.Post(() => { ApplyTheme(option); if (reopenMenu) OpenMenu(); });
            }, option == mode);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new(index == 0 ? 0 : 3, 0, index == 2 ? 0 : 3, 0);
            Grid.SetColumn(button, index);
            row.Children.Add(button);
        }
        return reopenMenu ? row : C.Stack(C.Space(row, 6), C.Text(mode == "System" ? "Following your system · " + (Theme.Dark ? "Dark" : "Light") : (Theme.Dark ? "Dark" : "Light") + " appearance", 11, "Muted"));
    }
    public void Navigate(string destination)
    {
        CurrentPage = destination;
        menu.IsVisible = false;
        search.Text = "";
        bool home = destination == "Home";
        pageTitle.Text = Title(destination);
        slash.IsVisible = pageTitle.IsVisible = !home;
        search.IsVisible = Searchable.Contains(destination);
        header.BorderThickness = new(0, 0, 0, home ? 0 : 1);
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
        // Home is a full-window radar; every other page scrolls inside the centered 1120 px column.
        if (home)
        {
            content.Content = null;
            body.Child = page.View;
        }
        else
        {
            body.Child = scroll;
            content.Content = page.View;
            scroll.Offset = default;
            Motion.Enter(page.View);
        }
        page.Refresh();
    }
    public void RefreshPage() => page?.Refresh();
    public void UpdateStatus()
    {
        receiveToggle.Content = Engine.Running ? "Stop receiving" : "Start receiving";
        if (Engine.Running)
            Theme.SetButton(receiveToggle.Resources, "Glass", "Text", "GlassBorder", "Text/8");
        else
            Theme.SetButton(receiveToggle.Resources, "Accent", "OnAccent", "Accent", "#FFC567");
        networkStatus.Text = Engine.Running ? "Receiving · " + (Engine.Adapter?.Address.ToString() ?? Engine.Endpoint) : "Receiving off";
        networkDot.Fill = Theme.Brush(Engine.Running ? "Mint" : "Muted");
        Control avatar = C.Avatar(Store.Preferences.DeviceName, Profiles.Picture(Store), 40);
        avatar.Margin = new(0, 0, 12, 0);
        TextBlock name = C.Text(Store.Preferences.DeviceName, 14, bold: true);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel card = new();
        DockPanel.SetDock(avatar, Dock.Left);
        card.Children.Add(avatar);
        StackPanel text = C.Stack(name, C.Space(C.Text("This " + Platform, 12, "Muted"), 0, 2));
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
        Tell("Ready to connect. Open hopo on the other device and click its name." + (OperatingSystem.IsMacOS() ? " If macOS asks, allow incoming connections and local network access." : ""));
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
    /// <summary>Adds files and folders to the selection, then opens Send unless <paramref name="stay"/> keeps the current page (the Home Send panel).</summary>
    public async Task AddSelectionsAsync(IEnumerable<string> paths, bool stay = false)
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
            if (stay)
                RefreshPage();
            else
                Navigate("Send");
            Tell($"{manifest.FileCount} files and {manifest.FolderCount} folders ready. Select a paired recipient.");
        }
        catch (Exception error) { Error(error); }
        finally { selecting = false; }
    }
    public void SendSelected(Peer peer, bool navigate = true)
    {
        _ = Engine.Send(peer, Selection.Select(item => item.Path));
        if (navigate)
            Navigate("Transfers");
        Tell("Transfer queued. The recipient must approve before any file bytes are sent.");
    }
    /// <summary>Sends the selection to a paired PC, or lists it on one approved phone's page only.</summary>
    public async Task SendToAsync(Peer? peer, BrowserDevice? device, bool navigate = true)
    {
        if (peer != null)
        {
            SendSelected(peer, navigate);
            return;
        }
        if (device == null || Selection.Count == 0)
            return;
        string[] paths = Selection.Select(item => item.Path).ToArray();
        await Task.Run(() => Engine.Browser.ShareAsync(paths, default, device.Handle));
        Tell("Sent to " + device.Label + ". It appears on that phone's page under Files from your PC.");
    }
    /// <summary>Turns browser sharing on, starting the receiver first when it is off.</summary>
    public void StartBrowserSharing()
    {
        Store.SetPreferences(Store.Preferences with
        {
            BrowserSharingEnabled = true
        });
        if (!Engine.Running)
            ToggleReceiving();
        else
            Engine.ApplySavedPreferences();
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
                foreach (string argument in new[] { "--app-name=hopo", title, body })
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
            NativeMenuItem open = new("Open hopo"), start = new("Start sharing server"), stop = new("Stop sharing server"), exit = new("Exit hopo");
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
                ToolTipText = "hopo — local file sharing",
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
                Notify("hopo is still running", "Receiving and sync continue. Use Exit hopo in the tray menu, or turn off keep-running in Settings.");
            }
            return;
        }
        exitRequested = false;
        if (closing)
            return;
        if (Engine.Transfers.Records.Any(record => !record.Terminal) && !await Dialogs.ConfirmAsync("Close hopo", "Stop active transfers and close hopo? Files already verified and saved stay on disk."))
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
                await CaptureAsync($"{mode}-{name.Replace(' ', '-').Replace("&", "and")}.png");
            }
            Navigate("Home");
            OpenMenu();
            await CaptureAsync(mode + "-Menu.png");
        }
        async Task CaptureAsync(string file)
        {
            await Task.Delay(250);
            Size size = Window.ClientSize;
            using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(new PixelSize((int)size.Width, (int)size.Height));
            bitmap.Render(Window);
            bitmap.Save(Path.Combine(folder, file), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
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
