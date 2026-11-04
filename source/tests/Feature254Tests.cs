using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    // Regression checks for the 2.5.3 break-test findings (IDs match docs/changes/2026-11-05-hopo-2.5.4-break-test-fixes.md).
    private static async Task Version254CasesAsync()
    {
        static bool Rejects(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException) { return true; }
        }
        static TransferRecord Record(int at, int files, int pathLength = 20) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Direction = "Received",
            PeerName = "Peer " + at,
            PeerFingerprint = new string('A', 64),
            State = TransferState.Declined,
            FileCount = files,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-at),
            Files = Enumerable.Range(0, files).Select(i => new TransferFile { RelativePath = new string('&', pathLength) + i, Size = 1 }).ToList()
        };
        // H1, M6: one huge record never makes metadata.json unreadable, and the other records stay.
        MetadataStore big = Store("History budget");
        big.SetHistory(Enumerable.Range(1, 50).Select(i => Record(i, 3)).Append(Record(0, 10000, 2040)));
        long size = new FileInfo(Path.Combine(big.Root, "metadata.json")).Length;
        MetadataStore reopened = new(big.Root);
        TransferRecord huge = reopened.History.Single(r => r.FileCount == 10000);
        Check(size < 16 * 1024 * 1024 && reopened.History.Length == 51 && huge.Files.Count <= MetadataStore.HistoryFiles, "A transfer with 10,000 long paths keeps metadata.json readable and keeps the other 50 records");
        // M4: a missing metadata.json is recovered from metadata.json.bak, keeping the identity.
        MetadataStore identity = Store("Missing main file");
        identity.SetCertificate("ABCDEF");
        identity.SetCertificate("ABCDEF");
        File.Delete(Path.Combine(identity.Root, "metadata.json"));
        MetadataStore recovered = new(identity.Root);
        Check(recovered.Thumbprint == "ABCDEF" && recovered.RecoveryNotice != null, "A missing metadata.json is restored from its saved copy instead of creating a new identity");
        // M5: read-only files no longer block saving; a briefly locked file is waited for, not rolled back.
        MetadataStore readOnly = Store("Read-only metadata");
        File.SetAttributes(Path.Combine(readOnly.Root, "metadata.json"), FileAttributes.ReadOnly);
        readOnly.SetPreferences(readOnly.Preferences with
        {
            DeviceName = "Still saves"
        });
        Check(new MetadataStore(readOnly.Root).Preferences.DeviceName == "Still saves", "A read-only metadata.json no longer stops saving or startup");
        MetadataStore locked = Store("Locked metadata");
        locked.SetPreferences(locked.Preferences with
        {
            DeviceName = "Newest"
        });
        FileStream holder = new(Path.Combine(locked.Root, "metadata.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        Task<MetadataStore> opening = Task.Run(() => new MetadataStore(locked.Root));
        await Task.Delay(800);
        holder.Dispose();
        MetadataStore afterLock = await opening;
        Check(afterLock.Preferences.DeviceName == "Newest" && afterLock.RecoveryNotice == null, "A metadata.json held briefly by another program is waited for instead of rolling back to the older copy");
        // L8: one bad saved field resets only that field; L9: duplicate or incomplete history records load safely.
        MetadataStore fields = Store("Field reset");
        fields.SetPreferences(fields.Preferences with
        {
            DeviceName = "Keep this name",
            LocalName = "family-share"
        });
        string path = Path.Combine(fields.Root, "metadata.json");
        string text = (await File.ReadAllTextAsync(path)).Replace(JsonSerializer.Serialize(fields.Preferences.ReceiveFolder).Trim('"'), "relative");
        await File.WriteAllTextAsync(path, text);
        MetadataStore reset = new(fields.Root);
        Check(reset.Preferences.DeviceName == "Keep this name" && reset.Preferences.LocalName == "family-share" && Path.IsPathFullyQualified(reset.Preferences.ReceiveFolder), "A bad receive folder in saved settings resets only that setting");
        TransferRecord twin = Record(1, 2);
        MetadataStore twins = Store("Duplicate history");
        twins.SetHistory([twin, twin with { CreatedUtc = twin.CreatedUtc.AddSeconds(1) }]);
        string history = Path.Combine(twins.Root, "metadata.json");
        await File.WriteAllTextAsync(history, (await File.ReadAllTextAsync(history)).Replace("\"Files\":[", "\"Files\":null,\"X\":["));
        MetadataStore twinsLoaded = new(twins.Root);
        TransferRegistry registry = new(twinsLoaded);
        Check(registry.Records.Length == 1 && registry.Records[0].Files != null, "Duplicate or incomplete history records load without stopping startup");
        // L8: receive folders that would only fail later are refused when saved.
        string missingDrive = Enumerable.Range('D', 23).Select(c => (char)c + ":\\").FirstOrDefault(drive => !Directory.Exists(drive)) ?? "Q:\\";
        Check(Rejects(() => MetadataStore.CheckReceiveFolder(missingDrive + "Inbox")) && Rejects(() => MetadataStore.CheckReceiveFolder(Path.GetPathRoot(root)!)) && Rejects(() => MetadataStore.CheckReceiveFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Inbox"))) && Rejects(() => MetadataStore.CheckReceiveFolder(Path.Combine(root, new string('x', 220)))), "Missing drives, drive roots, Windows folders and very long receive folders are refused when saved");
        // M3: damaged sync or notes files no longer stop the app.
        string side = Path.Combine(root, "Damaged side stores");
        Directory.CreateDirectory(side);
        await File.WriteAllTextAsync(Path.Combine(side, "sync-state.json"), "{\"Links\":null}");
        await File.WriteAllTextAsync(Path.Combine(side, "shared-notes.json"), "[{\"Id\":");
        SyncStore sync = new(side);
        SharedNotes notes = new(side);
        Check(sync.Links.Length == 0 && sync.RecoveryNotice != null && File.Exists(Path.Combine(side, "sync-state.json.damaged")) && notes.Snapshot().Notes.Length == 0, "Damaged sync and notes files start empty and are kept aside instead of stopping the app");
        // I1: notes refuse text-direction overrides but keep ordinary line breaks.
        Check(Rejects(() => SharedNotes.Validate("invoice\u202Etxt.exe")) && SharedNotes.Validate("line one\nline two") == "line one\nline two", "Notes refuse right-to-left override characters and keep line breaks");
        // L1: a long name says it is too long.
        bool tooLong = false;
        try
        {
            FileCatalog.ValidateSegment(new string('x', 200) + ".txt");
        }
        catch (InvalidDataException error) { tooLong = error.Message.Contains("too long", StringComparison.Ordinal); }
        Check(tooLong, "A file name over 180 characters is reported as too long");
        // M7: conflict copies of dotfiles get a valid name.
        string conflict = SyncFiles.ConflictPath("notes/.gitignore", new string('a', 64));
        Check(!Rejects(() => FileCatalog.ValidateRelative(conflict)) && conflict.EndsWith(").gitignore", StringComparison.Ordinal), "Conflict copies of dotfiles get a valid name");
        // L11: names match across Unicode composition and case.
        Check(SyncFiles.PathComparer.Instance.Equals("Cafe\u0301.txt", "CAFÉ.txt"), "Sync matches a Mac's decomposed accents with Windows names");
        // M8: one locked or unsupported entry is skipped and named; the rest of the folder still scans.
        string folder = Path.Combine(root, "Sync with problems");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "good.txt"), "good");
        await File.WriteAllTextAsync(Path.Combine(folder, "busy.pst"), "busy");
        File.WriteAllText(@"\\?\" + Path.Combine(folder, "trailing dot."), "odd");
        using (FileStream busy = new(Path.Combine(folder, "busy.pst"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using SyncScanner scanner = new();
            Manifest scanned = await scanner.ScanAsync(folder, CancellationToken.None, true);
            string[] skipped = scanner.Skipped(folder);
            Check(scanned.Entries.Any(e => e.RelativePath == "good.txt") && skipped.Any(s => s.StartsWith("busy.pst", StringComparison.Ordinal)) && skipped.Any(s => s.StartsWith("trailing dot.", StringComparison.Ordinal)), "A locked file and an unsupported name are skipped and named instead of stopping the folder scan");
        }
        File.Delete(@"\\?\" + Path.Combine(folder, "trailing dot."));
        // L6: parent folders are checked without a quadratic walk, and missing parents are still refused.
        Manifest deep = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Entries = Enumerable.Range(1, 400).Select(depth => new ManifestEntry { RelativePath = string.Join('/', Enumerable.Repeat("d", depth)), Directory = true }).ToList()
        };
        Manifest orphan = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Entries = [new ManifestEntry { RelativePath = "missing/file.txt", Size = 1, Sha256 = new string('a', 64) }]
        };
        Check(!Rejects(() => FileCatalog.ValidateManifest(deep)) && Rejects(() => FileCatalog.ValidateManifest(orphan)), "Manifest parent checks stay linear and still refuse missing parents");
        // L12: a certificate near its end is renewed for the same key; a future last-seen time is not shown online.
        using (Identity ephemeral = Identity.Ephemeral())
        using (System.Security.Cryptography.X509Certificates.X509Certificate2 renewed = Identity.Renew(ephemeral.Certificate))
            Check(Identity.KeyFingerprint(renewed) == ephemeral.Fingerprint && renewed.HasPrivateKey && renewed.NotAfter > DateTime.Now.AddYears(4), "A renewed device certificate keeps the same key, so pairings keep working");
        Check(!new Peer { Fingerprint = new string('B', 64), Name = "Clock", Address = "127.0.0.1", LastSeenUtc = DateTime.UtcNow.AddHours(1) }.Online, "A device seen while the clock was ahead is not shown online forever");
        // L4: a background check no longer undoes Stop trusting.
        MetadataStore trust = Store("Touch peer");
        Peer trusted = new()
        {
            Fingerprint = new string('C', 64),
            Name = "Dad's PC",
            Address = "127.0.0.1",
            Paired = true,
            Trusted = true
        };
        trust.UpsertPeer(trusted);
        trust.UpsertPeer(trusted with
        {
            Trusted = false
        });
        trust.TouchPeer(trusted.Fingerprint, "127.0.0.2", 48585, "");
        Check(trust.Peers.Single().Trusted == false && trust.Peers.Single().Address == "127.0.0.2", "A background check updates only where a PC was seen, so Stop trusting stays");
        // M10: each Windows app has its own registry entries.
        string users = Path.Combine(root, "hopoUsers.exe"), admin = Path.Combine(root, "hopoAdmin.exe");
        Check(DesktopRegistration.SettingsKeyFor(users) != DesktopRegistration.SettingsKeyFor(admin) && !DesktopRegistration.MenuKeysFor(users).Intersect(DesktopRegistration.MenuKeysFor(admin)).Any() && DesktopRegistration.RunValueFor(admin) == "hopo Admin" && DesktopRegistration.RunValueFor(users) == "Lantoss", "hopo Users and hopo Admin keep separate Explorer, settings and sign-in entries");
        // I2: one data folder is one instance, however it is spelled.
        Check(DesktopInstance.Key(root) == DesktopInstance.Key(root + Path.DirectorySeparatorChar), "A trailing separator does not start a second instance on the same data folder");
        // M2: a "connection reset" from an earlier reply does not end a UDP listener.
        using (UdpClient listener = new(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            using UdpClient closed = new(new IPEndPoint(IPAddress.Loopback, 0));
            IPEndPoint gone = (IPEndPoint)closed.Client.LocalEndPoint!;
            closed.Dispose();
            await listener.SendAsync(new byte[] { 1 }, gone);
            await Task.Delay(200);
            using UdpClient sender = new(new IPEndPoint(IPAddress.Loopback, 0));
            await sender.SendAsync(Encoding.ASCII.GetBytes("still here"), (IPEndPoint)listener.Client.LocalEndPoint!);
            using CancellationTokenSource wait = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult received = await LanDiscovery.ReceiveAsync(listener, wait.Token);
            Check(Encoding.ASCII.GetString(received.Buffer) == "still here", "A UDP listener keeps receiving after an ICMP port-unreachable for an earlier reply");
        }
        // M1: with "Ask before a new PC connects" on, Connect shows the code even when the other PC does not ask for it.
        using Identity firstKey = Identity.Ephemeral(), secondKey = Identity.Ephemeral();
        MetadataStore asking = SecureStore("Asks before connecting"), open = Store("Approval off");
        using TransferEngine client = new(asking, firstKey, true), server = new(open, secondKey, true);
        int shown = 0;
        client.ApprovePair = server.ApprovePair = (_, _) => { shown++; return Task.FromResult(false); };
        client.Start(Loopback, 0, false);
        server.Start(Loopback, 0, false);
        bool declined = false;
        try
        {
            await client.ConnectDirectAsync(new Peer { Fingerprint = secondKey.Fingerprint, Name = "Dad's PC", Address = "127.0.0.1", Port = server.Port, SupportsDirectConnect = true }, compare: client.ApprovePair);
        }
        catch (Exception error) when (error is ProtocolException or IOException) { declined = true; }
        Check(declined && shown == 1 && asking.Trusted(secondKey.Fingerprint) == null, "Connect shows the matching code when this PC asks before new PCs connect, even if the other PC does not");
    }
}
