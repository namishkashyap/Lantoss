using System.Buffers.Binary;
using System.IO.Pipes;
using Lantoss.Core;

internal static partial class Program
{
    private static async Task DesktopCasesAsync()
    {
        ShellSelectionCases();
        string executable = Path.Combine(root, "Installed app & photos", "Lantoss.exe");
        string key = new('A', 64);
        Peer saved = new()
        {
            Fingerprint = key,
            Name = "Family & Photos 😀",
            Address = "192.168.1.10",
            Paired = true
        };
        Peer blocked = saved with
        {
            Fingerprint = new('B', 64),
            Blocked = true
        };
        Peer nearby = saved with
        {
            Fingerprint = new('C', 64),
            Paired = false
        };
        RegistryText[] menu = DesktopRegistration.Menu(executable, [saved, blocked, nearby]);
        Check(menu.Count(item => item.Name == "MUIVerb" && item.Value == "Share with Lantoss") == 2 && menu.All(item => DesktopRegistration.MenuKeys.Any(root => item.Key == root || item.Key.StartsWith(root + "\\", StringComparison.Ordinal))), "Explorer registration is confined to Lantoss's two current-user file/folder menu roots");
        Check(menu.Count(item => item.Name == "MUIVerb" && item.Value == "Choose a PC…") == 2 && DesktopRegistration.Menu(executable, []).Any(item => item.Value == "Choose a PC…"), "The Explorer submenu has a recipient picker before any PC is paired");
        Check(menu.Any(item => item.Value == "Family && Photos 😀") && menu.Where(item => item.Key.EndsWith("\\command", StringComparison.Ordinal)).All(item => !item.Value.Contains("Family", StringComparison.Ordinal)), "Device labels preserve Unicode while escaping menu accelerators and stay out of launch commands");
        string command = '"' + executable + "\" --share-with " + key + " --share \"%1\"";
        Check(menu.Count(item => item.Value == command) == 2 && menu.All(item => !item.Key.Contains(blocked.Fingerprint, StringComparison.Ordinal) && !item.Key.Contains(nearby.Fingerprint, StringComparison.Ordinal)), "Named-PC verbs quote the app/file arguments, target saved fingerprints and omit blocked/unpaired PCs");
        Check(DesktopRegistration.Menu(executable, [saved with { Blocked = true }]).All(item => !item.Value.Contains("--share-with", StringComparison.Ordinal)), "Blocking or unpairing a PC removes its direct Explorer target");
        Check(DesktopRegistration.StartupCommand(executable) == '"' + executable + "\" --background", "Sign-in launches exactly the quoted installed executable in background mode");
        bool Rejected(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException) { return true; }
        }
        Check(Rejected(() => DesktopRegistration.Menu("Lantoss.exe", [])) && Rejected(() => DesktopRegistration.Menu(Path.Combine(root, "cmd.exe"), [])), "Desktop registration rejects a relative executable or a different program");
        string file = Path.Combine(root, "Photo & family 😀.txt");
        LaunchRequest request = LaunchRequest.Parse(["--share-with", key.ToLowerInvariant(), "--share", file]);
        Check(request.Kind == "share" && request.Paths.SequenceEqual([file]) && request.PeerKey == key, "Explorer launch parsing preserves quoted Unicode paths and normalizes the saved key");
        Check(LaunchRequest.Parse(["--background"]).Kind == "background" && LaunchRequest.Parse([]).Kind == "activate", "Background startup stays separate from foreground activation");
        Check(Rejected(() => LaunchRequest.Parse(["--share", "relative.txt"])) && Rejected(() => LaunchRequest.Parse(["--share-with", "192.168.1.2", "--share", file])) && Rejected(() => LaunchRequest.Parse(["--execute", file])) && Rejected(() => new LaunchRequest("share", null!).Checked()), "Launch requests reject relative paths, network-address recipients, unknown operations and null path lists");
        string[] excessive = Enumerable.Range(0, 256).Select(index => Path.Combine(root, new string('x', 800) + index)).ToArray();
        Check(Rejected(() => DesktopInstance.Encode(new("share", excessive))), "IPC rejects oversized path batches before sending them");
        MetadataStore preferences = Store("Tray preference");
        Check(preferences.Preferences.CloseToTray && preferences.Preferences.StartReceivingOnOpen, "Receiving starts automatically and closing to the tray is the default");
        preferences.SetPreferences(preferences.Preferences with
        {
            CloseToTray = false
        });
        Check(!new MetadataStore(preferences.Root).Preferences.CloseToTray, "The explicit close-to-exit preference survives restart");
        string data = Path.Combine(root, "single-instance-" + Guid.NewGuid().ToString("N"));
        List<LaunchRequest> received = [];
        using DesktopInstance server = new(data, item => { lock (received) received.Add(item); });
        try
        {
            await DesktopInstance.ForwardAsync(data, request);
        }
        catch (UnauthorizedAccessException)
        {
            skipped.Add("Current-user named-pipe connection denied by the development environment; forwarding and malformed-client recovery require an unrestricted Windows account. Production CurrentUserOnly protection is unchanged.");
            Check(DesktopInstance.Key(data) == DesktopInstance.Key(data.ToUpperInvariant()) && DesktopInstance.Key(data) != DesktopInstance.Key(data + "-other"), "Instance routing preserves the legacy case-insensitive per-store mutex identity");
            return;
        }
        await DesktopInstance.ForwardAsync(data, new("background", []));
        Check(received.Count == 2 && received[0].Paths.Single() == file && received[0].PeerKey == key && received[1].Kind == "background", "Actual current-user named-pipe forwarding delivers files, recipient and background intent to one instance");
        await using (NamedPipeClientStream malformed = new(".", DesktopInstance.PipeName(data), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await malformed.ConnectAsync(timeout.Token);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, DesktopInstance.MaxMessageBytes + 1);
            await malformed.WriteAsync(header, timeout.Token);
            await malformed.FlushAsync(timeout.Token);
        }
        await DesktopInstance.ForwardAsync(data, new("activate", []));
        Check(received.Count == 3 && received[^1].Kind == "activate", "An oversized IPC header is discarded and the running instance continues accepting valid requests");
        Check(DesktopInstance.Key(data) == DesktopInstance.Key(data.ToUpperInvariant()) && DesktopInstance.Key(data) != DesktopInstance.Key(data + "-other"), "Instance routing preserves the legacy case-insensitive per-store mutex identity");
    }
}
