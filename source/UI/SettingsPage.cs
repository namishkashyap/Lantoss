using System.Windows.Automation;
using Microsoft.Win32;

namespace Lantoss.UI;

internal sealed class SettingsPage : IPage
{
    private readonly AppShell app;
    public FrameworkElement View
    {
        get;
    }
    public SettingsPage(AppShell app)
    {
        this.app = app;
        Preferences saved = app.Store.Preferences;
        TextBox name = new()
        {
            Text = saved.DeviceName,
            MaxLength = 64,
            Margin = new(0, 8, 0, 18)
        };
        AutomationProperties.SetName(name, "This PC's device name");
        CheckBox notifications = new()
        {
            Content = "Show native completion notifications",
            IsChecked = saved.Notifications
        };
        CheckBox discovery = new()
        {
            Content = "Advertise this PC on the selected local network",
            IsChecked = saved.Discoverable
        };
        CheckBox automatic = new()
        {
            Content = "Find nearby PCs automatically when Lantoss opens",
            IsChecked = saved.StartReceivingOnOpen
        };
        CheckBox protection = new()
        {
            Content = "Require an access code for new connections",
            IsChecked = saved.RequireConnectionCode
        };
        CheckBox autoConnect = new()
        {
            Content = "Connect automatically to discoverable PCs without code protection",
            IsChecked = saved.AutoConnect
        };
        CheckBox browser = new()
        {
            Content = "Enable phone/browser sharing on this Wi-Fi",
            IsChecked = saved.BrowserSharingEnabled
        };
        CheckBox firewall = new()
        {
            Content = "Set up Private-network firewall access automatically",
            IsChecked = saved.AutoConfigureFirewall
        };
        string folder = saved.ReceiveFolder;
        TextBlock folderText = C.Text(folder, 12, "Muted");
        Button folderPicker = C.Button("Choose receive folder", () => { OpenFolderDialog dialog = new() { Title = "Choose the local receive folder" }; if (dialog.ShowDialog(app.Window) == true) { FileCatalog.CheckSafeDirectory(dialog.FolderName); folder = dialog.FolderName; folderText.Text = folder; } }, icon: "folder");
        Button save = C.Button("Save preferences", () =>
        {
            app.Store.SetPreferences(new()
            {
                DeviceName = name.Text,
                ReceiveFolder = folder,
                Theme = app.Store.Preferences.Theme,
                Notifications = notifications.IsChecked == true,
                Discoverable = discovery.IsChecked == true,
                StartReceivingOnOpen = automatic.IsChecked == true,
                RequireConnectionCode = protection.IsChecked == true,
                BrowserSharingEnabled = browser.IsChecked == true,
                AutoConnect = autoConnect.IsChecked == true,
                AutoConfigureFirewall = firewall.IsChecked == true
            });
            app.Engine.ApplySavedPreferences();
            if (firewall.IsChecked == true)
                _ = app.Firewall.EnsureAsync(!saved.AutoConfigureFirewall);
            app.ApplyTheme(app.Store.Preferences.Theme);
            app.UpdateStatus();
            app.Tell("Preferences saved. Discovery changes apply now. Existing jobs keep their chosen destination.");
        }, true);
        StackPanel general = C.Stack(C.Heading("General"), C.Text("Device name", 12, bold: true), name, C.Space(C.Text("Appearance", 12, bold: true), 8), C.Space(C.Text("Choose System, Light, or Dark in the sidebar. Changes save immediately; System follows Windows automatically.", 12, "Muted"), 16), C.Space(automatic, 7), C.Space(autoConnect, 7), C.Space(notifications, 12), save);
        StackPanel transfers = C.Stack(C.Heading("Transfers"), C.Space(C.Text("Files are saved locally on this PC", 12, bold: true), 9), C.Space(folderText, 15), C.Space(folderPicker, 17),
            C.Space(C.Text("Manual transfers require approval. Linked-folder sync uses its separate one-time host permission. Keep both is the default for manual duplicates.", 12, "Muted"), 15), C.Text("Closing Lantoss stops transfers and sync. Background receiving, transfer pause/resume, and bandwidth limits are not included in this release.", 11, "Muted"));
        NetworkAdapter[] available = NetworkAdapter.Available();
        ComboBox adapter = new()
        {
            ItemsSource = available,
            SelectedItem = available.FirstOrDefault(a => a.Address.Equals(app.Engine.Adapter?.Address)) ?? available.FirstOrDefault(),
            IsEnabled = !app.Engine.Running,
            Margin = new(0, 9, 0, 15)
        };
        AutomationProperties.SetName(adapter, "Receiving network adapter");
        Button toggle = C.Button(app.Engine.Running ? "Stop sharing" : "Start on this network", () => { if (app.Engine.Running) app.ToggleReceiving(); else if (adapter.SelectedItem is NetworkAdapter selected) app.StartReceiving(selected); else throw new IOException("Connect to private IPv4 Wi-Fi or Ethernet first."); app.UpdateStatus(); app.Navigate("Settings"); }, true);
        StackPanel network = C.Stack(C.Heading("Network"), C.Text("Wi-Fi or Ethernet adapter", 12, bold: true), adapter, C.Space(toggle, 17), C.Space(discovery, 12), C.Space(C.Text("Receiving address: " + app.Engine.Endpoint, 12, "Primary", true), 13),
            C.Text("Discovery uses multicast DNS plus LAN broadcast. Connections recover automatically when adapters change.", 12, "Muted"), C.Space(firewall, 8, 12), C.Text("Windows may ask for administrator approval once for this app's Private-network access. Public and Domain profiles are not opened.", 11, "Muted"), C.Space(C.Button("Connection diagnostics", () => NetworkDiagnostics.Show(app)), 0, 17));
        TextBlock key = C.Text(app.Engine.Fingerprint, 11, "Muted");
        key.FontFamily = new("Consolas");
        StackPanel security = C.Stack(C.Heading("Security & access"), C.Space(protection, 8), C.Space(browser, 14), C.Space(C.Text("Code protection is off by default. Open Phone & browser to view a current code when enabled. Codes expire after five minutes and one use; remembered PCs stay connected.", 12, "Muted"), 16),
            C.Space(C.Note("Native TLS · browser HTTP", "PC-to-PC transfers use TLS and saved public keys. Direct first connections trust the selected device without code comparison. The phone page uses local HTTP; an access code controls access without encrypting that browser link."), 18), C.Space(C.Text("This PC’s public-key fingerprint", 12, bold: true), 9), C.Space(key, 13), C.Small("Copy public fingerprint", () => { Clipboard.SetText(app.Engine.Fingerprint); app.Tell("Public fingerprint copied."); }));
        StackPanel advanced = C.Stack(C.Heading("Advanced & storage"), C.Space(C.Text("Local metadata: " + app.Store.Root, 11, "Muted"), 11), C.Space(C.Text("History retains up to 300 recent jobs within a 16 MB metadata budget. Clearing history never deletes received files. Preferences and transfer metadata use atomic JSON snapshots; file contents stay in the chosen local destinations.", 12, "Muted"), 13),
            C.Text("Shared Spaces supports native browsing and optional approved two-way sync. Conflict copies and previous versions are retained. Team roles, global auto-accept, launch at login, persistent background receiving, compression, and internet links are not implemented. Source symlinks, junctions, cloud placeholders, and NAS roots are rejected rather than followed.", 11, "Muted"));
        View = C.Stack(C.Space(C.Text("Make Lantoss feel at home.", 24, bold: true), 9), C.Space(C.Text("Local preferences, clear permissions, and security that stays on.", 13, "Muted"), 24), C.Space(C.Columns(C.Card(general), C.Card(transfers), 390), 20), C.Space(C.Columns(C.Card(network), C.Card(security), 390), 20), C.Card(advanced));
    }
    public void Refresh()
    {
    }
    private void Help() => MessageBox.Show(app.Window,
        "Open Lantoss 1.6 on both Windows PCs and use the same trusted Wi-Fi. Choose the other PC and click Connect. The default is direct connection without a password or comparison code. Files still require receiver approval.\n\nClick Open in browser for this PC's transfer page. For a phone, open Phone & browser and scan the QR or open its displayed link. Choose files for download, or create a Shared Space from a folder. Enable Read & upload on a space to allow approved uploads into it. Live transfers show measured speed and estimated time.\n\nEnable Require an access code if desired. The code is shown in Phone & browser. Browser sharing uses local HTTP, not encryption; use your trusted home Wi-Fi.\n\nIf Windows Firewall asks, allow Private networks only. If discovery fails, try Find nearby PCs or Manual connection. Check guest isolation, VPN routing, and sleep. Private-network firewall access is configured after Windows administrator approval. Router settings stay under your control.",
        "Connect your PCs", MessageBoxButton.OK, MessageBoxImage.Information);
}

internal sealed class ComingSoonPage : IPage
{
    public FrameworkElement View
    {
        get;
    }
    public ComingSoonPage(AppShell app, string name)
    {
        bool sync = name == "Sync";
        View = C.Stack(C.Space(C.Text(name, 27, bold: true), 10), C.Space(C.Badge("Planned · not enabled", "Muted", "Border"), 24),
            C.Empty(sync ? "Sync needs more than a shared folder." : "A shared workspace, when it is ready.",
                sync ? "Folder pairs, conflict copies, retained versions, deletion previews, schedules, and restore are planned for a later release. This release transfers explicitly selected files and folders." : "Shared folders, member roles, invitations, and audit events require a dedicated authorization backend. These features are not available in this release.",
                sync ? "sync" : "folder", C.Button("Send selected files instead", () => app.Navigate("Send"), true, "send")),
            C.Space(C.Note("Your files stay under your control", "No folders are monitored or shared by opening this screen. No cloud storage or internet availability is implied."), 0, 22));
    }
    public void Refresh()
    {
    }
}
