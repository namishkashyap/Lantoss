using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;

namespace Lantoss.Desktop;

internal sealed class PhonePage : IPage
{
    private readonly Shell app;
    private bool nameQr;
    private readonly StackPanel connection = new(), shared = new(), waiting = new();
    private readonly TextBlock accessCode = C.Text("", 23, "Primary", true), transferSpeed = C.Text("", 12, "Muted");
    private readonly StackPanel codeActions;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public Control View
    {
        get;
    }
    /// <summary>The phone link as a QR code on a white tile, in both appearances, so every phone camera can read it.</summary>
    public static Border Qr(string url, double size)
    {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using PngByteQRCode qr = new(data);
        using MemoryStream png = new(qr.GetGraphic(6));
        Image picture = new()
        {
            Source = new Bitmap(png),
            Width = size,
            Height = size
        };
        RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.None);
        return new Border { Child = picture, Background = Brushes.White, CornerRadius = new(size > 120 ? 14 : 12), Padding = new(size > 120 ? 8 : 6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    }
    public PhonePage(Shell app)
    {
        this.app = app;
        CheckBox requireCode = C.Check("Require an access code", app.Store.Preferences.RequireConnectionCode);
        requireCode.IsCheckedChanged += (_, _) =>
        {
            if (requireCode.IsChecked == app.Store.Preferences.RequireConnectionCode)
                return;
            app.Store.SetPreferences(app.Store.Preferences with
            {
                RequireConnectionCode = requireCode.IsChecked == true
            });
            app.Engine.ApplySavedPreferences();
            Refresh();
            app.Tell(requireCode.IsChecked == true ? "Code protection is on for new connections. Browsers need to reconnect." : "Direct connection is on. No password or comparison code is required.");
        };
        accessCode.FontFamily = new FontFamily("monospace");
        codeActions = C.Inline(C.Small("Copy current code", () => app.CopyAsync(app.Engine.PairingCode.Value, "Access code copied. It expires after five minutes and one use.")),
            C.Small("New code", () => { app.Engine.PairingCode.Rotate(); app.Engine.Browser.ResetConnections(); Refresh(); }));
        StackPanel protection = C.Stack(C.Heading("Access code"), C.Space(requireCode, 8),
            C.Space(C.Text("Off by default: new computers and phones are approved on this screen instead. Turn it on to require a code; entering it also approves the phone.", 12, "Muted"), 16), C.Eyebrow("Current code"), C.Space(accessCode, 12, 6), codeActions);
        StackPanel selected = C.Stack(C.Heading("Files your phone can download", C.Ghost(C.Small("Remove all", app.Engine.Browser.ClearFiles))),
            C.Space(C.Text("Only files you pick here are listed. Uploads from a phone land in your receive folder after you approve them.", 12, "Muted"), 18),
            C.Space(C.Inline(C.AsyncButton("Add files", PickFilesAsync, true, "plus"), C.AsyncButton("Add a folder", PickFolderAsync, icon: "folder")), 18), shared);
        View = C.Stack(C.Header("Share", "Your phone. No extra app.", "Same Wi‑Fi, scan the code, done. No app needed."),
            waiting, C.Space(transferSpeed, 18), C.Space(C.Columns(C.Card(connection), C.Card(protection), 370), 20), C.Space(C.Card(selected), 20),
            app.Store.Preferences.BrowserHttps ? C.Note("Browser sharing uses HTTPS", "The phone page is encrypted with this computer's own certificate. Phones show a warning the first time because no public authority signed it. Computer-to-computer transfers continue to use TLS.", "phone")
                : C.Note("Browser sharing uses local HTTP", "The phone page is not encrypted. Use your trusted home Wi-Fi. The optional code controls access; it does not add transport encryption. Computer-to-computer transfers continue to use TLS.", "phone"));
        timer.Tick += (_, _) => { UpdateCode(); UpdateSpeed(); };
        View.AttachedToVisualTree += (_, _) => timer.Start();
        View.DetachedFromVisualTree += (_, _) => timer.Stop();
    }
    public void Refresh()
    {
        waiting.Children.Clear();
        BrowserDevice[] asking = app.Engine.Browser.Devices.Where(b => !b.Approved).ToArray();
        if (asking.Length > 0)
            waiting.Children.Add(C.Space(C.Text(asking.Length == 1 ? "A browser is waiting for your Allow" : asking.Length + " browsers are waiting for your Allow", 15, bold: true), 10));
        foreach (BrowserDevice browser in asking)
            waiting.Children.Add(C.Space(DevicesPage.Waiting(app, browser), 18));
        connection.Children.Clear();
        connection.Children.Add(C.Heading("Open on your phone", app.Engine.Browser.Running ? C.Badge("Browser sharing on", "Mint", "Mint/12") : C.Badge("Browser sharing off", "Muted", "Text/6")));
        if (app.Engine.Browser.Running)
        {
            // The IP link works on every phone; many Android phones cannot resolve .local names, so it is the QR default.
            string ip = app.Engine.Browser.IpUrl, named = app.Engine.Browser.Url;
            bool hasName = named != ip;
            string url = nameQr && hasName ? named : ip;
            Border picture = Qr(url, 184);
            picture.Margin = new(0, 0, 0, 16);
            connection.Children.Add(picture);
            connection.Children.Add(C.Space(C.Text(url, 15, "Primary", true), 8));
            if (hasName && url == ip)
                connection.Children.Add(C.Space(C.Text("On iPhone, iPad and computers you can also type " + named, 12, bold: true), 8));
            connection.Children.Add(C.Space(C.Text(app.Engine.Browser.LocalAddressStatus, 12, "Muted"), 12));
            string print = app.Engine.Browser.CertificateFingerprint;
            if (app.Engine.Browser.Https)
                connection.Children.Add(C.Space(C.Stack(C.Space(C.Text("Encrypted (HTTPS)" + (print.Length >= 16 ? " · certificate " + print[..8] + " " + print[8..16] : ""), 12, "Success", true), 4),
                    C.Text("Phones show a certificate warning the first time; check this code in the browser's certificate details, then continue.", 11, "Muted")), 12));
            connection.Children.Add(C.Space(C.Inline(C.Small("Copy link", () => app.CopyAsync(url, "Browser link copied.")), C.Small("Open page", () => Shell.Open(url))), 14));
            Button change = C.Small("Change address", () => app.Navigate("Settings"));
            connection.Children.Add(C.Space(hasName ? C.Inline(C.Small(nameQr ? "Show IP QR (every phone)" : "Show name QR (iPhone, computers)", () => { nameQr = !nameQr; Refresh(); }), change) : C.Inline(change), 14));
            connection.Children.Add(C.Text(app.Store.Preferences.RequireConnectionCode ? "Scan, then enter the access code shown alongside." : app.Store.Preferences.ApproveNewDevices ? "Scan, then press Allow here when the phone appears." : "Scan and connect directly. No password or code.", 12, "Muted"));
        }
        else
        {
            connection.Children.Add(C.Space(C.Text("Start browser sharing to show the phone link and QR code.", 13, "Muted"), 18));
            connection.Children.Add(C.Button("Start browser sharing", () => { app.StartBrowserSharing(); Refresh(); }, true, "phone"));
        }
        UpdateCode();
        UpdateSpeed();
        shared.Children.Clear();
        if (app.Engine.Browser.Files.Length == 0)
            shared.Children.Add(C.Text("No individual files selected. Add files above, or use Shared Spaces to publish an explicitly chosen folder.", 12, "Muted"));
        foreach (BrowserFile file in app.Engine.Browser.Files.Take(100))
            shared.Children.Add(C.Space(C.Card(C.Heading(file.Name + "  ·  " + Format.Bytes(file.Size), C.Small("Remove", () => app.Engine.Browser.Remove(file.Id)), 12), 13), 9));
    }
    private void UpdateCode()
    {
        bool enabled = app.Store.Preferences.RequireConnectionCode;
        accessCode.Text = enabled ? app.Engine.PairingCode.Value : "No code required";
        codeActions.IsVisible = enabled;
    }
    private void UpdateSpeed()
    {
        TransferRecord[] active = app.Engine.Transfers.Records.Where(record => record.Transport.Contains("HTTP", StringComparison.Ordinal) && !record.Terminal).ToArray();
        transferSpeed.Text = active.Length == 0 ? "No active browser transfers. Live upload and download speeds appear here." : $"{active.Length} active browser transfers · {active.Sum(record => record.BytesPerSecond) / 1048576:0.00} MB/s";
    }
    private async Task PickFilesAsync() => await AddAsync(await app.PickFilesAsync("Choose files for phone/browser download"));
    private async Task PickFolderAsync() => await AddAsync(await app.PickFoldersAsync("Choose a folder of files for browser download"));
    private async Task AddAsync(string[] paths)
    {
        if (paths.Length == 0)
            return;
        app.Tell("Adding selected browser files…");
        await Task.Run(() => app.Engine.Browser.ShareAsync(paths));
        Refresh();
        app.Tell("Selected files are now listed in the browser page.");
    }
}

internal sealed class NotesPage : IPage
{
    private sealed record Destination(string Label, Peer? Peer)
    {
        public override string ToString() => Label;
    }
    private readonly Shell app;
    private readonly TextBox draft = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130, MaxHeight = 250, MaxLength = SharedNotes.MaxCharacters, PlaceholderText = "Type a note or paste a link" };
    private readonly ComboBox target = new() { MinWidth = 260, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel notes = new();
    public Control View
    {
        get;
    }
    public NotesPage(Shell app)
    {
        this.app = app;
        Button send = C.AsyncButton("Share text or link", async () =>
        {
            string content = SharedNotes.Validate(draft.Text);
            if (target.SelectedItem is Destination { Peer: { } peer })
                await app.Engine.SendNoteAsync(peer, content);
            else
                app.Engine.Browser.Notes.Add(content, "This " + Shell.Platform, local: true);
            if ((draft.Text ?? "").Trim() == content)
                draft.Text = "";
            Refresh();
            app.Tell("Text shared. Find it in Text & links on the receiving computer or its browser page.");
        }, true, "send");
        View = C.Stack(C.Header("Share", "Share text or a link", "Send a note or link to a nearby device."),
            C.Space(C.Card(C.Stack(C.Space(C.Eyebrow("Message"), 8), C.Space(draft, 16), C.Space(C.Eyebrow("Send to"), 8), C.Inline(target, send), C.Space(C.Text("Up to 16,000 characters. Notes kept here are visible to connected browsers; an access code applies if you've turned one on. Nothing opens or copies automatically.", 11, "Muted"), 0, 12))), 28),
            C.Heading("Shared on this " + Shell.Platform, C.Small("Refresh", Refresh)), notes);
    }
    public void Refresh()
    {
        // Refresh only the list and recipients. Incoming notes never replace a typed draft. Text on Home preselects that PC once.
        string? selected = app.NotePeerKey ?? (target.SelectedItem as Destination)?.Peer?.Fingerprint;
        app.NotePeerKey = null;
        Destination[] destinations = [new("This " + Shell.Platform + " · keep for my phone", null), .. app.Engine.Peers.Where(p => p.Paired && !p.Blocked).Select(p => new Destination(p.Name, p))];
        target.ItemsSource = destinations;
        target.SelectedItem = destinations.FirstOrDefault(item => item.Peer?.Fingerprint == selected) ?? destinations[0];
        notes.Children.Clear();
        SharedNote[] visible = app.Engine.Browser.Notes.Snapshot().Notes.Where(note => note.Content.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visible.Length == 0)
            notes.Children.Add(C.Card(C.Text("Shared text and links will appear here.", 13, "Muted")));
        foreach (SharedNote note in visible)
        {
            StackPanel actions = C.Inline(C.Small("Copy", () => app.CopyAsync(note.Content, "Copied.")), C.Small("Delete", () => app.Engine.Browser.Notes.Remove(note.Id)));
            // Only http/https notes get an explicit open action; text is never executed or opened automatically.
            if (note.Kind == "link")
                actions.Children.Add(C.Small("Open link", async () => { if (SharedNote.Link(note.Content) is Uri link && await app.Dialogs.ConfirmAsync("Open link", SharedNote.OpenPrompt(link, note.Sender), "Open")) Shell.Open(link.AbsoluteUri); }));
            notes.Children.Add(C.Space(C.Card(C.Stack(C.Space(C.Text(note.Content, 14), 12), C.Space(C.Text(note.Sender + " · " + note.CreatedUtc.ToLocalTime().ToString("dd MMM, HH:mm"), 11, "Muted"), 12), actions), 18), 12));
        }
    }
}
