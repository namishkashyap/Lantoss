using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task SecurityCasesAsync(Identity first, Identity second)
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
        string rlo = new((char)0x202E, 1), zws = new((char)0x200B, 1), zwj = new((char)0x200D, 1), slash = new((char)92, 1);
        Check(Rejects(() => FileCatalog.ValidateSegment("invoice" + rlo + "fdp.exe")) && Rejects(() => FileCatalog.ValidateSegment("a" + zws + "b.txt")) && Rejects(() => MetadataStore.ValidateName("Dad" + rlo + "s PC")), "Direction overrides and zero-width spaces are refused in file and device names");
        Check(MetadataStore.ValidateName("Family" + zwj + "PC").Length == 9, "ZWJ stays allowed for Indic scripts and emoji names");
        Check(FileCatalog.Defuse(Path.Combine(root, "photo.url")).EndsWith("photo.url.txt", StringComparison.Ordinal) && FileCatalog.Defuse("desktop.ini") == "desktop.ini.txt" && FileCatalog.Defuse("photo.jpg") == "photo.jpg", "Received shell-control files are kept as inert text; ordinary files keep their names");
        Check(FileCatalog.Risky(new Manifest { Id = Guid.NewGuid().ToString("N"), Entries = [new ManifestEntry { RelativePath = "setup.exe", Size = 1 }] }) && !FileCatalog.Risky(new Manifest { Id = Guid.NewGuid().ToString("N"), Entries = [new ManifestEntry { RelativePath = "photo.jpg", Size = 1 }] }), "Batches with programs are flagged; photos are not");
        byte[] Png(int width) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width, 0, 0, 0, 1];
        Check(Profiles.IsPicture(Png(256)) && !Profiles.IsPicture(Png(50000)), "Profile pictures that declare huge dimensions are refused before decoding");
        Check(SharedNote.Link("https://example.com/a") != null && SharedNote.Link("https:" + slash + slash + "evil/share") == null && SharedNote.Link("javascript:alert(1)") == null && SharedNote.Link("file:///C:/Windows") == null && SharedNote.Link("https://example.com/" + rlo) == null, "Only parsed http/https links can be opened");
        Check(SharedNote.LocalTarget(new Uri("http://192.168.1.5/")) && SharedNote.LocalTarget(new Uri("http://nas/")) && !SharedNote.LocalTarget(new Uri("https://example.com/")), "Links into the home network are flagged before opening");
        string n1 = new('1', 64), n2 = new('2', 64);
        Check(Identity.Comparison(first.Fingerprint, second.Fingerprint, n1, n2) == Identity.Comparison(second.Fingerprint, first.Fingerprint, n1, n2) && Identity.Comparison(first.Fingerprint, second.Fingerprint, n1, n2) != Identity.Comparison(first.Fingerprint, second.Fingerprint, n1, n1) && Identity.Commit(n1) != Identity.Commit(n2) && Identity.Commit(n1).Length == 64, "Pairing v2 codes depend on both nonces and the client commits before seeing the server nonce");
        string leak = BrowserPortal.PublicError(new UnauthorizedAccessException("Access to the path 'C:" + slash + "Users" + slash + "priya" + slash + "secret.txt' is denied."));
        Check(!leak.Contains("priya", StringComparison.Ordinal) && BrowserPortal.PublicError(new InvalidDataException("Choose between 1 and 100 files.")) == "Choose between 1 and 100 files.", "Errors sent to other devices never contain local paths or user names");
        MetadataStore capped = SecureStore("Peer cap");
        for (int index = 0; index < 256; index++)
            capped.UpsertPeer(new Peer { Fingerprint = index.ToString("X64"), Name = "PC " + index, Address = "192.168.1.9", Paired = true, KeyVerified = false, LastSeenUtc = DateTime.UtcNow.AddMinutes(index) });
        capped.UpsertPeer(new Peer { Fingerprint = new string('F', 64), Name = "Real PC", Address = "192.168.1.8", Paired = true });
        Check(capped.Peers.Length == 256 && capped.Trusted(new string('F', 64)) != null && capped.Trusted(0.ToString("X64")) == null, "A full device list keeps working and gives up its oldest unverified record");
        string metadata = Path.Combine(capped.Root, "metadata.json");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("\"Real PC\"", "\"Real" + slash + "u202EPC\"", StringComparison.Ordinal));
        Check(new MetadataStore(capped.Root).Trusted(new string('F', 64)) == null, "Records with hidden characters are dropped when a tampered store loads");
        SharedNotes notes = new(capped.Root);
        notes.Add("Written on this PC", "This PC", local: true);
        for (int index = 0; index < 120; index++)
            notes.Add("Remote " + index, "Phone");
        Check(notes.Snapshot().Notes.Length == 100 && notes.Snapshot().Notes.Any(note => note.Local && note.Content == "Written on this PC"), "Notes from other devices never push out notes written on this PC");
        string syncRoot = Path.Combine(root, "Security sync");
        Directory.CreateDirectory(syncRoot);
        string synced = Path.Combine(syncRoot, "a.txt");
        File.WriteAllText(synced, "start");
        for (int index = 0; index < 8; index++)
        {
            string temporary = Path.Combine(syncRoot, ".flowshare-" + index + ".part");
            File.WriteAllText(temporary, "version " + index);
            await SyncFiles.CommitAsync(temporary, syncRoot, "a.txt", await SyncFiles.HashAsync(synced, CancellationToken.None), CancellationToken.None);
        }
        Check(Directory.EnumerateFiles(Path.Combine(syncRoot, ".flowshare-versions"), "*.bak", SearchOption.AllDirectories).Count() == SyncFiles.KeptVersions && File.ReadAllText(synced) == "version 7", "Sync keeps only the newest five versions, so rewrites cannot fill the disk");
        TransferRegistry jobs = new(capped);
        string reused = Guid.NewGuid().ToString("N");
        jobs.Add(new TransferRecord { Id = reused, Direction = "Sent", PeerName = "PC", PeerFingerprint = "browser" }).State(TransferState.Completed);
        jobs.Add(new TransferRecord { Id = reused, Direction = "Sent", PeerName = "PC", PeerFingerprint = "browser" }).State(TransferState.Completed);
        Check(jobs.Records.Count(record => record.Id == reused) == 1, "Finished jobs leave memory and their identity can be reused");
        SharedSpace limited = new(Guid.NewGuid().ToString("N"), "Photos", root, AllowedPeers: [first.Fingerprint]);
        Check(limited.Allows(first.Fingerprint) && !limited.Allows(second.Fingerprint) && new SharedSpace(limited.Id, "Photos", root).Allows(second.Fingerprint), "A space can be limited to chosen PCs; existing spaces stay open to all paired PCs");
        await SecurePortalAsync();
        await SecurePairingAsync(first, second);
    }
    private static async Task SecurePortalAsync()
    {
        MetadataStore store = SecureStore("Secure portal");
        // HTTPS is optional since 2.1.3 (phones warn about the self-signed certificate); these checks cover it when it is on.
        store.SetPreferences(store.Preferences with
        {
            BrowserHttps = true
        });
        PairingCode code = new();
        using BrowserPortal portal = new(store, new(store), code, true);
        BrowserDevice? asked = null;
        portal.ApprovalRequested += device => asked = device;
        portal.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(true, request.DefaultFolder));
        await portal.StartAsync(Loopback, 0);
        Check(portal.Https && portal.IpUrl.StartsWith("https://127.0.0.1:", StringComparison.Ordinal) && portal.HttpUrl.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && portal.CertificateFingerprint.Length == 64, "With HTTPS on, the phone page uses it and the PC can show its certificate fingerprint");
        using (HttpClient plain = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = new Uri(portal.HttpUrl)
        })
        {
            HttpResponseMessage redirect = await plain.GetAsync("/");
            HttpResponseMessage api = await plain.GetAsync("/api/session");
            Check(redirect.StatusCode == HttpStatusCode.Redirect && redirect.Headers.Location?.ToString() == portal.IpUrl + "/" && api.StatusCode == HttpStatusCode.Forbidden, "Plain HTTP only redirects to the encrypted page and never serves the API");
        }
        HttpResponseMessage headers;
        using HttpClient phone = BrowserClient(portal.IpUrl);
        HttpResponseMessage first = await phone.GetAsync("/api/session");
        string cookie = string.Join(";", first.Headers.GetValues("Set-Cookie"));
        Check(cookie.Contains("__Host-LantossSession=", StringComparison.Ordinal) && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "The phone session cookie is Secure, HttpOnly, SameSite=Strict and host-locked");
        await ConnectBrowser(phone);
        JsonElement waiting = await Body(await phone.GetAsync("/api/session"));
        HttpResponseMessage hidden = await phone.GetAsync("/api/files");
        HttpResponseMessage profile = await phone.PostAsJsonAsync("/api/profile", new
        {
            name = "Priya",
            picture = (string?)null
        });
        Check(waiting.GetProperty("waiting").GetBoolean() && !waiting.GetProperty("connected").GetBoolean() && waiting.GetProperty("encrypted").GetBoolean() && hidden.StatusCode == HttpStatusCode.Forbidden && profile.IsSuccessStatusCode && asked != null && portal.Devices.Single() is { Approved: false, Name: "Priya" }, "A new phone waits for Allow: it can show its name but cannot see files until the PC approves it");
        string handle = portal.Devices.Single().Handle;
        portal.Approve(handle);
        headers = await phone.GetAsync("/api/files");
        Check(headers.IsSuccessStatusCode && headers.Headers.Contains("Permissions-Policy") && headers.Headers.Contains("Cross-Origin-Opener-Policy") && headers.Headers.GetValues("Content-Security-Policy").Single().Contains("require-trusted-types-for", StringComparison.Ordinal), "After Allow the phone opens files, and every response carries the hardening headers");
        using HttpClient other = BrowserClient(portal.IpUrl);
        await ConnectBrowser(other);
        string otherHandle = portal.Devices.Single(device => device.Handle != handle).Handle;
        portal.Approve(otherHandle);
        string photo = Path.Combine(store.Root, "For Priya.jpg");
        await File.WriteAllTextAsync(photo, "A file for one phone.");
        await portal.ShareAsync([photo], default, handle);
        JsonElement mine = await Body(await phone.GetAsync("/api/files")), theirs = await Body(await other.GetAsync("/api/files"));
        string id = mine.EnumerateArray().Single().GetProperty("id").GetString()!;
        Check(mine.EnumerateArray().Single().GetProperty("personal").GetBoolean() && theirs.GetArrayLength() == 0 && (await other.GetAsync("/api/download/" + id)).StatusCode == HttpStatusCode.NotFound && await (await phone.GetAsync("/api/download/" + id)).Content.ReadAsStringAsync() == "A file for one phone.", "Files sent to one phone appear only on that phone");
        int accepted = 0;
        for (int index = 0; index < BrowserPortal.NotesPerWindow + 1; index++)
            if ((await phone.PostAsJsonAsync("/api/notes", new
            {
                content = "note " + index
            })).IsSuccessStatusCode)
                accepted++;
        Check(accepted == BrowserPortal.NotesPerWindow, "One device can post a limited number of notes, so it cannot wipe the board");
        JsonElement offer = await Body(await phone.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "link.url", size = 4 } }
        }));
        JsonElement saved = await Body(await phone.PutAsync("/api/uploads/" + offer.GetProperty("id").GetString() + "/0", new ByteArrayContent("[x]x"u8.ToArray())));
        string received = Path.Combine(store.Preferences.ReceiveFolder, "link.url.txt");
        bool marked = !OperatingSystem.IsWindows() || File.ReadAllText(received + ":Zone.Identifier").Contains("ZoneId=3", StringComparison.Ordinal);
        Check(saved.GetProperty("name").GetString() == "link.url.txt" && File.Exists(received) && marked, "A received shortcut is saved as inert text and marked as coming from another device");
        portal.Block(otherHandle);
        bool refused;
        try
        {
            using HttpClient again = BrowserClient(portal.IpUrl);
            refused = !(await again.GetAsync("/api/session")).IsSuccessStatusCode;
        }
        catch (HttpRequestException) { refused = true; }
        Check(refused && portal.BlockedAddresses.Contains("127.0.0.1") && portal.Devices.All(device => device.Handle != otherHandle), "Block disconnects the browser and refuses its address");
        portal.Unblock("127.0.0.1");
        store.SetPreferences(store.Preferences with
        {
            RequireConnectionCode = true
        });
        portal.ApplySecurityPolicy();
        using HttpClient coded = BrowserClient(portal.IpUrl);
        coded.DefaultRequestHeaders.Add("Origin", portal.IpUrl);
        JsonElement login = await Body(await coded.PostAsJsonAsync("/api/login", new
        {
            code = code.Value
        }));
        Check(login.GetProperty("connected").GetBoolean() && !login.GetProperty("waiting").GetBoolean(), "Entering the access code also approves the phone");
        portal.Stop();
        await portal.WhenStopped;
    }
    private static async Task SecurePairingAsync(Identity first, Identity second)
    {
        MetadataStore left = SecureStore("Secure sender"), right = SecureStore("Secure receiver");
        Check(left.Preferences.ApproveNewDevices && !left.Preferences.BrowserHttps, "New installs approve new devices and serve the phone page over HTTP by default");
        using TransferEngine sender = new(left, first, true), receiver = new(right, second, true);
        string? receiverCode = null, senderCode = null;
        receiver.ApprovePair = (request, _) => { receiverCode = request.ComparisonCode; return Task.FromResult(true); };
        sender.Start(Loopback, 0, false);
        receiver.Start(Loopback, 0, false);
        Peer nearby = new()
        {
            Fingerprint = second.Fingerprint,
            Name = "Secure receiver",
            Address = "127.0.0.1",
            Port = receiver.Port,
            SupportsDirectConnect = true
        };
        bool silent = false;
        try
        {
            await sender.ConnectDirectAsync(nearby);
            silent = true;
        }
        catch (ProtocolException) { }
        Check(!silent && left.Trusted(second.Fingerprint) == null && right.Trusted(first.Fingerprint) == null, "A new PC cannot pair silently: the connecting PC must confirm too");
        Peer paired = await sender.ConnectDirectAsync(nearby, "", default, (request, _) => { senderCode = request.ComparisonCode; return Task.FromResult(true); });
        Check(paired.KeyVerified && right.Trusted(first.Fingerprint) is { KeyVerified: true } && senderCode != null && senderCode == receiverCode, "First pairing is confirmed on both screens with matching v2 comparison codes");
        string folder = Path.Combine(right.Root, "Shared photos");
        Directory.CreateDirectory(folder);
        SharedSpace space = receiver.Browser.Spaces.Add(folder);
        receiver.Browser.Spaces.SetAllowed(space.Id, [new string('B', 64)]);
        int hiddenCount = (await sender.GetRemoteSpacesAsync(paired)).Length;
        receiver.Browser.Spaces.SetAllowed(space.Id, null);
        Check(hiddenCount == 0 && (await sender.GetRemoteSpacesAsync(paired)).Length == 1, "A paired PC sees a space only when the space allows it");
    }
}
