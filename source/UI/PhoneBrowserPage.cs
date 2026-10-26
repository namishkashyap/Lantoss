using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QRCoder;

namespace Lantoss.UI;

internal sealed class PhoneBrowserPage : IPage
{
    private readonly AppShell app;
    private bool nameQr;
    private readonly StackPanel connection = new(), shared = new();
    private readonly TextBlock accessCode = C.Text("", 23, "Primary", true);
    private readonly TextBlock transferSpeed = C.Text("", 12, "Muted");
    private readonly StackPanel codeActions;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public FrameworkElement View
    {
        get;
    }
    public PhoneBrowserPage(AppShell app)
    {
        this.app = app;
        CheckBox requireCode = new()
        {
            Content = "Require an access code",
            IsChecked = app.Store.Preferences.RequireConnectionCode
        };
        requireCode.Click += (_, _) =>
        {
            app.Store.SetPreferences(app.Store.Preferences with
            {
                RequireConnectionCode = requireCode.IsChecked == true
            });
            app.Engine.ApplySavedPreferences();
            Refresh();
            app.Tell(requireCode.IsChecked == true ? "Code protection is on for new PC/browser connections. Browsers need to reconnect." : "Direct connection is on. No password or comparison code is required.");
        };
        accessCode.FontFamily = new("Consolas");
        Button copyCode = C.Small("Copy current code", () => { Clipboard.SetText(app.Engine.PairingCode.Value); app.Tell("Access code copied. It expires after five minutes and one use."); });
        Button newCode = C.Small("New code", () => { app.Engine.PairingCode.Rotate(); app.Engine.Browser.ResetConnections(); Refresh(); });
        newCode.Margin = new(8, 0, 0, 0);
        codeActions = C.Inline(copyCode, newCode);
        StackPanel protection = C.Stack(C.Heading("Optional protection"), C.Space(requireCode, 8), C.Space(C.Text("Off by default: nearby PCs connect directly and phones open the page without a code. Turn it on to require a code for new devices and browser sessions.", 12, "Muted"), 13), C.Space(accessCode, 12), codeActions);
        Button files = C.Button("Share files with browser", PickFiles, true, "plus");
        Button folders = C.Button("Add a folder", PickFolder, icon: "folder");
        folders.Margin = new(9, 0, 0, 0);
        StackPanel selected = C.Stack(C.Heading("Files your phone can download", C.Small("Remove all", () => app.Engine.Browser.ClearFiles())),
            C.Space(C.Text("Only files you select here are listed. Uploads from a phone go to your receive folder after your approval.", 12, "Muted"), 18), C.Space(C.Inline(files, folders), 18), shared);
        View = C.Stack(C.Space(C.Text("Your phone. No extra app.", 26, bold: true), 9),
            C.Space(C.Text("Use the same Wi-Fi, scan the QR code with your phone’s camera, and open the page. Send photos/files to the PC or download the files you share below.", 13, "Muted"), 23),
            C.Space(transferSpeed, 18), C.Space(C.Columns(C.Card(connection), C.Card(protection), 370), 20), C.Space(C.Card(selected), 20),
            C.Note("Browser sharing uses local HTTP", "The phone page is not encrypted. Use your trusted home Wi-Fi. The optional code controls access; it does not add transport encryption. Native PC-to-PC transfers continue to use TLS.", "devices"));
        timer.Tick += (_, _) => { UpdateCode(); UpdateSpeed(); };
        View.Loaded += (_, _) => timer.Start();
        View.Unloaded += (_, _) => timer.Stop();
    }
    public void Refresh()
    {
        connection.Children.Clear();
        connection.Children.Add(C.Heading("Open on your phone", C.Badge(app.Engine.Browser.Running ? "Browser is on" : "Browser is off")));
        if (app.Engine.Browser.Running)
        {
            // The IP link works on every phone; many Android phones cannot resolve .local names, so it is the QR default.
            string ip = app.Engine.Browser.IpUrl, named = app.Engine.Browser.Url;
            bool hasName = named != ip;
            string url = nameQr && hasName ? named : ip;
            using QRCodeGenerator generator = new();
            using QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using PngByteQRCode qr = new(data);
            using MemoryStream image = new(qr.GetGraphic(6));
            Image picture = new()
            {
                Source = BitmapFrame.Create(image, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
                Width = 184,
                Height = 184,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new(0, 0, 0, 16)
            };
            System.Windows.Automation.AutomationProperties.SetName(picture, "QR code for " + url);
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            connection.Children.Add(picture);
            connection.Children.Add(C.Space(C.Text(url, 15, "Primary", true), 8));
            if (hasName && url == ip)
                connection.Children.Add(C.Space(C.Text("On iPhone, iPad and computers you can also type " + named, 12, bold: true), 8));
            connection.Children.Add(C.Space(C.Text(app.Engine.Browser.LocalAddressStatus, 12, "Muted"), 12));
            Button copy = C.Small("Copy link", () => { Clipboard.SetText(url); app.Tell("Browser link copied."); });
            Button open = C.Small("Open page", () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
            open.Margin = new(8, 0, 0, 0);
            connection.Children.Add(C.Space(C.Inline(copy, open), 14));
            Button change = C.Small("Change address", () => app.Navigate("Settings"));
            if (hasName)
            {
                Button swap = C.Small(nameQr ? "Show IP QR (every phone)" : "Show name QR (iPhone, computers)", () => { nameQr = !nameQr; Refresh(); });
                change.Margin = new(8, 0, 0, 0);
                connection.Children.Add(C.Space(C.Inline(swap, change), 14));
            }
            else
                connection.Children.Add(C.Space(change, 14));
            connection.Children.Add(C.Text(app.Store.Preferences.RequireConnectionCode ? "Scan, then enter the access code shown alongside." : "Scan and connect directly. No password or code.", 12, "Muted"));
        }
        else
        {
            connection.Children.Add(C.Space(C.Text("Start browser sharing to show the phone link and QR code.", 13, "Muted"), 18));
            connection.Children.Add(C.Button("Start browser sharing", () =>
            {
                app.Store.SetPreferences(app.Store.Preferences with
                {
                    BrowserSharingEnabled = true
                });
                if (!app.Engine.Running)
                    app.ToggleReceiving();
                else
                    app.Engine.ApplySavedPreferences();
                Refresh();
            }, true, "devices"));
        }
        UpdateCode();
        UpdateSpeed();
        shared.Children.Clear();
        if (app.Engine.Browser.Files.Length == 0)
            shared.Children.Add(C.Text("No individual files selected. Add files above, or use Shared Spaces to publish an explicitly chosen folder.", 12, "Muted"));
        foreach (BrowserFile file in app.Engine.Browser.Files.Take(100))
            shared.Children.Add(C.Space(C.Card(C.Heading(file.Name + "  ·  " + Format.Bytes(file.Size), C.Small("Remove", () => app.Engine.Browser.Remove(file.Id)), 12), 13), 9));
        if (app.Engine.Browser.Files.Length > 100)
            shared.Children.Add(C.Text("Showing the first 100 entries. All selected files are available in the browser.", 11, "Muted"));
    }
    private void UpdateCode()
    {
        bool enabled = app.Store.Preferences.RequireConnectionCode;
        accessCode.Text = enabled ? app.Engine.PairingCode.Value : "No code required";
        codeActions.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateSpeed()
    {
        TransferRecord[] active = app.Engine.Transfers.Records.Where(record => record.Transport.Contains("HTTP", StringComparison.Ordinal) && !record.Terminal).ToArray();
        transferSpeed.Text = active.Length == 0 ? "No active browser transfers. Open the browser for live upload/download speeds." : $"{active.Length} active browser transfers · {active.Sum(record => record.BytesPerSecond) / 1048576:0.00} MB/s";
    }
    private async void PickFiles()
    {
        OpenFileDialog picker = new()
        {
            Title = "Choose files for phone/browser download",
            Multiselect = true
        };
        if (picker.ShowDialog(app.Window) == true)
            await AddAsync(picker.FileNames);
    }
    private async void PickFolder()
    {
        OpenFolderDialog picker = new()
        {
            Title = "Choose a folder of files for browser download"
        };
        if (picker.ShowDialog(app.Window) == true)
            await AddAsync([picker.FolderName]);
    }
    private async Task AddAsync(string[] paths)
    {
        try
        {
            app.Tell("Adding selected browser files…");
            await Task.Run(() => app.Engine.Browser.ShareAsync(paths));
            Refresh();
            app.Tell("Selected files are now listed in the browser page.");
        }
        catch (Exception error) { app.Error(error); }
    }
}
