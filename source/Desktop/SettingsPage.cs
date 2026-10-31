using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Lantoss.Desktop;

internal sealed class SettingsPage : IPage
{
    private readonly Shell app;
    public Control View
    {
        get;
    }
    public SettingsPage(Shell app)
    {
        this.app = app;
        Preferences saved = app.Store.Preferences;
        TextBox name = new()
        {
            Text = saved.DeviceName,
            MaxLength = 64,
            Margin = new(0, 8, 0, 18)
        };
        TextBox address = new()
        {
            Text = saved.LocalName,
            MaxLength = 46,
            Margin = new(0, 8, 0, 6)
        };
        ContentControl picture = new()
        {
            Content = C.Avatar(saved.DeviceName, Profiles.Picture(app.Store), 56),
            Margin = new(0, 0, 16, 0)
        };
        Button choose = C.Small("Choose picture", async () =>
        {
            string[] files = await app.PickFilesAsync("Choose a profile picture", false, [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] }]);
            if (files.Length == 0)
                return;
            SavePicture(files[0], Profiles.PicturePath(app.Store));
            picture.Content = C.Avatar(app.Store.Preferences.DeviceName, Profiles.Picture(app.Store), 56);
            app.UpdateStatus();
        });
        Button remove = C.Small("Remove", () =>
        {
            File.Delete(Profiles.PicturePath(app.Store));
            picture.Content = C.Avatar(app.Store.Preferences.DeviceName, null, 56);
            app.UpdateStatus();
        });
        DockPanel pictureRow = new();
        DockPanel.SetDock(picture, Dock.Left);
        pictureRow.Children.Add(picture);
        StackPanel pictureActions = C.Stack(C.Space(C.Text("Shown in the menu, on other devices and on the phone page.", 12, "Muted"), 8), C.Inline(choose, remove));
        pictureActions.VerticalAlignment = VerticalAlignment.Center;
        pictureRow.Children.Add(pictureActions);
        static CheckBox Check(string text, bool value) => C.Check(text, value);
        CheckBox automatic = Check("Start the sharing server automatically when hopo opens", saved.StartReceivingOnOpen),
            tray = Check("Keep receiving and syncing when I close the window", saved.CloseToTray),
            autoConnect = Check("Connect automatically to new computers (only when approval is off)", saved.AutoConnect),
            notifications = Check("Show notifications for incoming and finished transfers", saved.Notifications),
            senderFolders = Check("Save received files in a folder named after the sender", saved.SenderFolders),
            discovery = Check("Let nearby computers discover this one", saved.Discoverable),
            protection = Check("Require an access code for new connections", saved.RequireConnectionCode),
            browser = Check("Enable phone/browser sharing on this Wi-Fi", saved.BrowserSharingEnabled),
            approve = Check("Ask before a new PC or phone connects", saved.ApproveNewDevices),
            https = Check("Encrypt the phone page (HTTPS)", saved.BrowserHttps);
        string folder = saved.ReceiveFolder;
        TextBlock folderText = C.Text(folder, 12, "Muted");
        Button folderPicker = C.AsyncButton("Choose receive folder", async () =>
        {
            string[] picked = await app.PickFoldersAsync("Choose the local receive folder");
            if (picked.Length == 0)
                return;
            FileCatalog.CheckSafeDirectory(picked[0]);
            folder = picked[0];
            folderText.Text = folder;
        }, icon: "folder");
        Button save = C.Button("Save changes", () =>
        {
            app.Store.SetPreferences(app.Store.Preferences with
            {
                DeviceName = name.Text ?? "",
                ReceiveFolder = folder,
                Notifications = notifications.IsChecked == true,
                Discoverable = discovery.IsChecked == true,
                StartReceivingOnOpen = automatic.IsChecked == true,
                RequireConnectionCode = protection.IsChecked == true,
                BrowserSharingEnabled = browser.IsChecked == true,
                ApproveNewDevices = approve.IsChecked == true,
                BrowserHttps = https.IsChecked == true,
                AutoConnect = autoConnect.IsChecked == true,
                CloseToTray = tray.IsChecked == true,
                SenderFolders = senderFolders.IsChecked == true,
                LocalName = address.Text ?? ""
            });
            app.Engine.ApplySavedPreferences();
            app.UpdateStatus();
            app.Tell("Preferences saved. Discovery changes apply now. Existing jobs keep their chosen destination.");
        }, true);
        StackPanel general = C.Stack(C.Heading("General"), C.Text("Profile picture", 12, bold: true), C.Space(pictureRow, 18, 8), C.Text("Name shown to other devices", 12, bold: true), name,
            C.Space(C.Text("Appearance", 12, bold: true), 8), C.Space(app.AppearancePicker(), 16), automatic, tray, autoConnect, notifications);
        StackPanel transfers = C.Stack(C.Heading("Transfers"), C.Space(C.Text("Files are saved locally on this " + Shell.Platform, 12, bold: true), 9), C.Space(folderText, 15), C.Space(folderPicker, 17), C.Space(senderFolders, 15),
            C.Text("Manual transfers require approval. Linked-folder sync uses its separate one-time host permission. Keep both is the default for manual duplicates.", 12, "Muted"));
        NetworkAdapter[] available = NetworkAdapter.Available();
        ComboBox adapter = new()
        {
            ItemsSource = available,
            SelectedItem = available.FirstOrDefault(a => a.Address.Equals(app.Engine.Adapter?.Address)) ?? available.FirstOrDefault(),
            IsEnabled = !app.Engine.Running,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(0, 9, 0, 15)
        };
        Button toggle = C.Button(app.Engine.Running ? "Stop sharing" : "Start sharing server", () =>
        {
            if (app.Engine.Running)
                app.ToggleReceiving();
            else if (adapter.SelectedItem is NetworkAdapter selected)
                app.StartReceiving(selected);
            else
                throw new IOException("Connect to private IPv4 Wi-Fi or Ethernet first.");
            app.Navigate("Settings");
        }, true);
        StackPanel network = C.Stack(C.Heading("Network"), C.Text("Wi-Fi or Ethernet adapter", 12, bold: true), adapter, C.Space(toggle, 17), C.Space(discovery, 12), C.Text("Phone page address", 12, bold: true), address, C.Space(C.Text("Phones and computers can open http://<address>.local. Use letters, digits and hyphens. The QR code in Phone & browser always works.", 11, "Muted"), 14),
            C.Space(C.Text("Receiving address: " + app.Engine.Endpoint, 12, "Primary", true), 13),
            C.Space(C.Text("Discovery uses multicast DNS plus LAN broadcast. Connections recover automatically when adapters change.", 12, "Muted"), 14),
            C.AsyncButton("Connection diagnostics", app.Dialogs.DiagnosticsAsync));
        TextBlock key = C.Text(app.Engine.Fingerprint, 11, "Muted");
        key.FontFamily = new FontFamily("monospace");
        StackPanel security = C.Stack(C.Heading("Security & access"), C.Space(approve, 4), C.Space(protection, 4), C.Space(browser, 4), C.Space(https, 2), C.Space(C.Text("Phones show a one-time certificate warning; accept it once.", 11, "Muted"), 14),
            C.Space(C.Text("Code protection is off by default. Open Phone & browser to view the current code when it is on. Codes expire after five minutes and one use, and both screens confirm a matching code.", 12, "Muted"), 16),
            C.Space(C.Note(saved.BrowserHttps ? "Native TLS · browser HTTPS" : "Native TLS · browser HTTP", saved.BrowserHttps ? "Computer-to-computer transfers use TLS and saved public keys. The phone page uses HTTPS with this computer's own certificate." : "Computer-to-computer transfers use TLS and saved public keys. The phone page uses local HTTP; an access code controls access without encrypting that browser link."), 18),
            C.Space(C.Text("This device's public-key fingerprint", 12, bold: true), 9), C.Space(key, 13), C.Small("Copy fingerprint", () => app.CopyAsync(app.Engine.Fingerprint, "Fingerprint copied.")));
        StackPanel advanced = C.Stack(C.Heading("Advanced & storage"), C.Space(C.Text("Local data: " + app.Store.Root, 11, "Muted"), 11),
            C.Text("History keeps up to 300 recent jobs. Clearing history never deletes received files. The device key is stored in this folder, readable only by your user account.", 12, "Muted"));
        View = C.Stack(C.Header("hopo", "Settings", "Preferences, permissions and security.", save),
            C.Space(C.Columns(C.Card(general), C.Card(transfers), 390), 20), C.Space(C.Columns(C.Card(network), C.Card(security), 390), 20), C.Card(advanced));
    }
    public void Refresh()
    {
    }
    /// <summary>Center-crops to a 128 px square and re-encodes as PNG, so only a small, freshly encoded image is stored and served.</summary>
    private static void SavePicture(string source, string target)
    {
        using FileStream input = File.OpenRead(source);
        using Bitmap image = Bitmap.DecodeToWidth(input, 384, BitmapInterpolationMode.HighQuality);
        for (int size = 128; size >= 64; size -= 32)
        {
            double side = Math.Min(image.Size.Width, image.Size.Height);
            using RenderTargetBitmap square = new(new PixelSize(size, size));
            using (DrawingContext context = square.CreateDrawingContext())
                context.DrawImage(image, new Rect((image.Size.Width - side) / 2, (image.Size.Height - side) / 2, side, side), new Rect(0, 0, size, size));
            using MemoryStream output = new();
            square.Save(output, PngBitmapEncoderOptions.Default);
            byte[] data = output.ToArray();
            if (!Profiles.IsPicture(data))
                continue;
            File.WriteAllBytes(target + ".tmp", data);
            File.Move(target + ".tmp", target, true);
            return;
        }
        throw new InvalidDataException("This picture could not be made small enough. Choose another one.");
    }
}
