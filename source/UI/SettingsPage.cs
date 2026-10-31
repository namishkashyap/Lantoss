using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Lantoss.UI;

internal sealed class SettingsPage : IPage
{
    /// <summary>Center-crops to a 128 px square and re-encodes, so only a small, freshly encoded image is stored and served.</summary>
    private static void SavePicture(string source, string target)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 384;
        image.UriSource = new Uri(source);
        image.EndInit();
        int side = Math.Min(image.PixelWidth, image.PixelHeight);
        BitmapSource square = new TransformedBitmap(new CroppedBitmap(image, new Int32Rect((image.PixelWidth - side) / 2, (image.PixelHeight - side) / 2, side, side)), new ScaleTransform(128.0 / side, 128.0 / side));
        byte[] data = Encode(new PngBitmapEncoder(), square);
        if (!Profiles.IsPicture(data))
            data = Encode(new JpegBitmapEncoder { QualityLevel = 85 }, square);
        if (!Profiles.IsPicture(data))
            throw new InvalidDataException("This picture could not be made small enough. Choose another one.");
        File.WriteAllBytes(target + ".tmp", data);
        File.Move(target + ".tmp", target, true);
    }
    private static byte[] Encode(BitmapEncoder encoder, BitmapSource source)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }
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
        TextBox address = new()
        {
            Text = saved.LocalName,
            MaxLength = 46,
            Margin = new(0, 8, 0, 6)
        };
        AutomationProperties.SetName(address, "Phone page address");
        ContentControl picture = new()
        {
            Content = C.Avatar(saved.DeviceName, Profiles.Picture(app.Store), 56),
            Focusable = false,
            Margin = new(0, 0, 16, 0)
        };
        Button choosePicture = C.Small("Choose picture", () =>
        {
            OpenFileDialog dialog = new()
            {
                Title = "Choose a profile picture",
                Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (dialog.ShowDialog(app.Window) != true)
                return;
            try
            {
                SavePicture(dialog.FileName, Profiles.PicturePath(app.Store));
            }
            catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or FormatException) { app.Error(error); return; }
            picture.Content = C.Avatar(app.Store.Preferences.DeviceName, Profiles.Picture(app.Store), 56);
            app.UpdateStatus();
        });
        Button removePicture = C.Small("Remove", () =>
        {
            try
            {
                File.Delete(Profiles.PicturePath(app.Store));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { app.Error(error); return; }
            picture.Content = C.Avatar(app.Store.Preferences.DeviceName, null, 56);
            app.UpdateStatus();
        });
        removePicture.Margin = new(7, 0, 0, 0);
        DockPanel pictureRow = new();
        DockPanel.SetDock(picture, Dock.Left);
        pictureRow.Children.Add(picture);
        StackPanel pictureActions = C.Stack(C.Space(C.Text("Shown in the menu and on the phone/browser page.", 12, "Muted"), 8), C.Inline(choosePicture, removePicture));
        pictureActions.VerticalAlignment = VerticalAlignment.Center;
        pictureRow.Children.Add(pictureActions);
        CheckBox senderFolders = new()
        {
            Content = "Save received files in a folder named after the sender",
            IsChecked = saved.SenderFolders,
            Margin = new(0, 0, 0, 15)
        };
        CheckBox notifications = new()
        {
            Content = "Show Windows notifications for incoming and finished transfers",
            IsChecked = saved.Notifications
        };
        CheckBox startup = new()
        {
            Content = "Launch hopo in the tray when I sign in",
            IsChecked = !app.Preview && app.Integration?.LaunchAtSignIn == true,
            IsEnabled = !app.Preview
        };
        CheckBox explorer = new()
        {
            Content = "Show Share with Lantoss in Explorer's right-click menu",
            IsChecked = !app.Preview && app.Integration?.ShellEnabled == true,
            IsEnabled = !app.Preview
        };
        CheckBox tray = new()
        {
            Content = "Keep receiving and syncing in the tray when I close the window",
            IsChecked = saved.CloseToTray
        };
        AutomationProperties.SetName(startup, "Launch hopo at sign-in");
        AutomationProperties.SetName(explorer, "Explorer sharing menu");
        CheckBox discovery = new()
        {
            Content = "Advertise this PC on the selected local network",
            IsChecked = saved.Discoverable
        };
        CheckBox automatic = new()
        {
            Content = "Start the sharing server automatically when hopo opens",
            IsChecked = saved.StartReceivingOnOpen
        };
        CheckBox protection = new()
        {
            Content = "Require an access code for new connections",
            IsChecked = saved.RequireConnectionCode
        };
        CheckBox autoConnect = new()
        {
            Content = "Connect automatically to new PCs (only when approval is off)",
            IsChecked = saved.AutoConnect
        };
        CheckBox approve = new()
        {
            Content = "Ask before a new PC or phone connects",
            IsChecked = saved.ApproveNewDevices
        };
        CheckBox https = new()
        {
            Content = "Encrypt the phone page (HTTPS)",
            IsChecked = saved.BrowserHttps
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
            if (!app.Preview && app.Integration != null)
            {
                app.Integration.LaunchAtSignIn = startup.IsChecked == true;
                app.Integration.SetShellEnabled(explorer.IsChecked == true, app.Store.Peers);
            }
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
                ApproveNewDevices = approve.IsChecked == true,
                BrowserHttps = https.IsChecked == true,
                AutoConfigureFirewall = firewall.IsChecked == true,
                CloseToTray = tray.IsChecked == true,
                SenderFolders = senderFolders.IsChecked == true,
                LocalName = address.Text
            });
            app.Engine.ApplySavedPreferences();
            if (firewall.IsChecked == true)
                _ = app.Firewall.EnsureAsync(!saved.AutoConfigureFirewall);
            app.ApplyTheme(app.Store.Preferences.Theme);
            app.UpdateStatus();
            app.Tell("Preferences saved. Discovery changes apply now. Existing jobs keep their chosen destination.");
        }, true);
        StackPanel general = C.Stack(C.Heading("General"), C.Text("Profile picture", 12, bold: true), C.Space(pictureRow, 18, 8), C.Text("Name shown to other devices", 12, bold: true), name, C.Space(C.Text("Appearance", 12, bold: true), 8), C.Space(C.Text("Choose System, Light, or Dark in the menu. Changes save immediately; System follows Windows automatically.", 12, "Muted"), 16), C.Space(startup, 7), C.Space(automatic, 7), C.Space(tray, 7), C.Space(explorer, 7), C.Space(C.Text("Windows 11: Share with Lantoss appears in the main menu. Choose a remembered PC or open the device picker.", 11, "Muted"), 12), C.Space(autoConnect, 7), C.Space(notifications, 12), save);
        StackPanel transfers = C.Stack(C.Heading("Transfers"), C.Space(C.Text("Files are saved locally on this PC", 12, bold: true), 9), C.Space(folderText, 15), C.Space(folderPicker, 17), senderFolders,
            C.Space(C.Text("Manual transfers require approval. Linked-folder sync uses its separate one-time host permission. Keep both is the default for manual duplicates.", 12, "Muted"), 15), C.Text("Closing the window keeps the server running when the tray option is enabled. Use Exit hopo in the tray to stop it. Transfer pause/resume and bandwidth limits are not included.", 11, "Muted"));
        NetworkAdapter[] available = NetworkAdapter.Available();
        ComboBox adapter = new()
        {
            ItemsSource = available,
            SelectedItem = available.FirstOrDefault(a => a.Address.Equals(app.Engine.Adapter?.Address)) ?? available.FirstOrDefault(),
            IsEnabled = !app.Engine.Running,
            Margin = new(0, 9, 0, 15)
        };
        AutomationProperties.SetName(adapter, "Receiving network adapter");
        Button toggle = C.Button(app.Engine.Running ? "Stop sharing" : "Start sharing server", () => { if (app.Engine.Running) app.ToggleReceiving(); else if (adapter.SelectedItem is NetworkAdapter selected) app.StartReceiving(selected); else throw new IOException("Connect to private IPv4 Wi-Fi or Ethernet first."); app.UpdateStatus(); app.Navigate("Settings"); }, true);
        StackPanel network = C.Stack(C.Heading("Network"), C.Text("Wi-Fi or Ethernet adapter", 12, bold: true), adapter, C.Space(toggle, 17), C.Space(discovery, 12), C.Text("Phone page address", 12, bold: true), address, C.Space(C.Text("Phones and computers can open http://<address>.local. Use letters, digits and hyphens. The QR code in Phone & browser always works.", 11, "Muted"), 14), C.Space(C.Text("Receiving address: " + app.Engine.Endpoint, 12, "Primary", true), 13),
            C.Text("Discovery uses multicast DNS plus LAN broadcast. Connections recover automatically when adapters change.", 12, "Muted"), C.Space(firewall, 8, 12), C.Text("Windows may ask for administrator approval once for this app's Private-network access. Public and Domain profiles are not opened.", 11, "Muted"), C.Space(C.Button("Connection diagnostics", () => NetworkDiagnostics.Show(app)), 0, 17));
        TextBlock key = C.Text(app.Engine.Fingerprint, 11, "Muted");
        key.FontFamily = new("Consolas");
        StackPanel security = C.Stack(C.Heading("Security & access"), C.Space(approve, 8), C.Space(C.Text("A new PC is confirmed on both screens and a new phone waits for Allow. Paired PCs reconnect without asking. Turn this off only on a network where you trust every device.", 11, "Muted"), 12), C.Space(protection, 8), C.Space(browser, 8), C.Space(https, 8), C.Space(C.Text("Phones show a certificate warning once; check the code shown in Phone & browser, then continue.", 11, "Muted"), 12), C.Space(C.Text("Codes expire after five minutes and one use. The access code also approves the phone that enters it.", 12, "Muted"), 16),
            C.Space(C.Note("Native TLS · phone HTTPS", "PC-to-PC transfers use TLS and saved public keys. The phone page is encrypted with this PC's own certificate; plain HTTP and the .local name only redirect to it. Received files are marked as coming from another device."), 18), C.Space(C.Text("This PC’s public-key fingerprint", 12, bold: true), 9), C.Space(key, 13), C.Small("Copy public fingerprint", () => { Clipboard.SetText(app.Engine.Fingerprint); app.Tell("Public fingerprint copied."); }));
        StackPanel advanced = C.Stack(C.Heading("Advanced & storage"), C.Space(C.Text("Local metadata: " + app.Store.Root, 11, "Muted"), 11), C.Space(C.Text("History retains up to 300 recent jobs within a 16 MB metadata budget. Clearing history never deletes received files. Preferences and transfer metadata use atomic JSON snapshots; file contents stay in the chosen local destinations.", 12, "Muted"), 13),
            C.Space(C.Text("Shared Spaces supports native browsing and optional approved two-way sync. Conflict copies and previous versions are retained. Team roles, global auto-accept, compression, and internet links are not implemented. Source symlinks, junctions, cloud placeholders, and NAS roots are rejected rather than followed.", 11, "Muted"), 13),
            C.Small("Remove Explorer menu trust", () =>
            {
                if (MessageBox.Show(app.Window, "Stop this PC trusting the “Lantoss Development” publisher for app packages? Windows will ask for administrator approval.\n\nThe Windows 11 sharing menu will ask for this trust again the next time it is turned on.", "Remove Explorer menu trust", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                try
                {
                    ExplorerPackage.RemovePublisherTrust();
                    app.Tell("Explorer menu publisher trust removed from this PC.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException) { app.Tell("Publisher trust was not removed. " + error.Message); }
            }));
        View = C.Stack(C.Hero("hopo", "Settings", "Preferences, permissions and security."), C.Space(C.Columns(C.Card(general), C.Card(transfers), 390), 20), C.Space(C.Columns(C.Card(network), C.Card(security), 390), 20), C.Card(advanced));
    }
    public void Refresh()
    {
    }
    private void Help() => MessageBox.Show(app.Window,
        "Open hopo on both Windows PCs and use the same trusted Wi-Fi. Choose the other PC and click Connect. The default is direct connection without a password or comparison code. Files still require receiver approval.\n\nClick Open in browser for this PC's transfer page. For a phone, open Phone & browser and scan the QR or open its displayed link. Choose files for download, or create a Shared Space from a folder. Enable Read & upload on a space to allow approved uploads into it. Live transfers show measured speed and estimated time.\n\nEnable Require an access code if desired. The code is shown in Phone & browser. Browser sharing uses local HTTP, not encryption; use your trusted home Wi-Fi.\n\nIf Windows Firewall asks, allow Private networks only. If discovery fails, try Find nearby PCs or Manual connection. Check guest isolation, VPN routing, and sleep. Private-network firewall access is configured after Windows administrator approval. Router settings stay under your control.",
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
