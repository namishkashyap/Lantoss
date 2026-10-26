using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task LocalAddressCasesAsync()
    {
        string key = new('A', 64);
        DateTime now = DateTime.UtcNow;
        IPAddress address = IPAddress.Parse("192.168.1.10"), other = IPAddress.Parse("192.168.1.20");
        LocalNameClaim one = new(address, key), two = new(other, new string('B', 64));
        for (int tick = 0; tick < 30; tick++)
        {
            DateTime time = now.AddMilliseconds(tick * 250);
            // Deliver both sides' actual encoded DNS probes/announcements in a simultaneous startup.
            NameAction[] first = one.Tick(time), second = two.Tick(time);
            foreach (NameAction action in first)
                _ = two.Observe(LocalDns.Parse(action.Probe ? LocalDns.Probe(action.Host, address) : LocalDns.Answer([action.Host], address, action.Ttl)), time);
            foreach (NameAction action in second)
                _ = one.Observe(LocalDns.Parse(action.Probe ? LocalDns.Probe(action.Host, other) : LocalDns.Answer([action.Host], other, action.Ttl)), time);
        }
        Check(one.Ready && two.Ready && one.OwnsAlias != two.OwnsAlias, "Two simultaneous PCs claim unique device names and exactly one shared no-port hostname");
        LocalNameClaim owner = one.OwnsAlias ? one : two, waiting = one.OwnsAlias ? two : one;
        IPAddress ownerAddress = one.OwnsAlias ? address : other;
        DateTime stopped = now.AddSeconds(8);
        _ = waiting.Observe(LocalDns.Parse(LocalDns.Answer([LocalNameClaim.SharedHost], ownerAddress, 0)), stopped);
        for (int i = 1; i < 9; i++)
            waiting.Tick(stopped.AddMilliseconds(250 * i));
        Check(waiting.OwnsAlias, "A waiting PC automatically takes the shared hostname after the owner sends its goodbye");
        LocalNameClaim occupied = new(address, key);
        _ = occupied.Observe(LocalDns.Parse(LocalDns.Answer([LocalNameClaim.SharedHost], other, 30)), now);
        for (int i = 0; i < 10; i++)
            occupied.Tick(now.AddMilliseconds(i * 250));
        Check(occupied.Ready && !occupied.OwnsAlias, "An existing local alias is respected while this PC's unique address remains available");
        string previousName = occupied.Host;
        _ = occupied.Observe(LocalDns.Parse(LocalDns.Answer([previousName], other)), now.AddSeconds(3));
        Check(occupied.Host != previousName && !occupied.Ready, "A duplicate device hostname is renamed and re-probed instead of claimed twice");
        byte[] answer = LocalDns.Answer([LocalNameClaim.SharedHost, LocalNameClaim.DeviceHost(key)], address, negativeIpv6: true);
        Check(LocalDns.Parse(answer).Addresses.Length == 2 && LocalDns.Parse(answer).Addresses.All(a => a.Address.Equals(address)), "DNS wire responses advertise the actual interface address and include IPv6 negative records");
        byte[] loop = new byte[18];
        loop[5] = 1;
        loop[12] = 0xc0;
        loop[13] = 12;
        bool invalid = false;
        try
        {
            LocalDns.Parse(loop);
        }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid, "Cyclic DNS name compression is rejected with bounded parsing");
        Check(LocalNameClaim.DeviceUrlAllowed("http://" + LocalNameClaim.DeviceHost(key), key) && !LocalNameClaim.DeviceUrlAllowed("http://attacker.invalid", key) && !LocalNameClaim.DeviceUrlAllowed("http://" + LocalNameClaim.DeviceHost(key) + ":1234", key), "Discovered browser links accept only that device's no-port local hostname");
        bool Claimed(string host) => host.Equals(LocalNameClaim.SharedHost, StringComparison.OrdinalIgnoreCase);
        Check(LocalBrowserAccess.HostAllowed("http://192.168.1.10", "SHARE-WOLLVO.LOCAL.", 80, Claimed)
            && !LocalBrowserAccess.HostAllowed("http://192.168.1.10", "attacker.invalid", 80, Claimed)
            && !LocalBrowserAccess.HostAllowed("http://192.168.1.10", LocalNameClaim.SharedHost, 1234, Claimed), "Friendly-host access remains an explicit name and listening-port allowlist");
        Check(LocalBrowserAccess.SameOrigin("http://share-wollvo.local", "share-wollvo.local", 80, "same-origin")
            && !LocalBrowserAccess.SameOrigin("http://attacker.invalid", "share-wollvo.local", 80, "same-origin")
            && !LocalBrowserAccess.SameOrigin("http://share-wollvo.local/path", "share-wollvo.local", 80, "same-origin")
            && !LocalBrowserAccess.SameOrigin("http://share-wollvo.local", "share-wollvo.local", 80, "cross-site"), "Local names preserve exact-origin protection against cross-site writes");
        Peer? discovered = MdnsDiscovery.ParseAnnouncement(MdnsDiscovery.BuildAnnouncement(address, key, "Family PC", 48585, browserUrl: "http://" + LocalNameClaim.DeviceHost(key)), address);
        Check(discovered?.BrowserUrl == "http://" + LocalNameClaim.DeviceHost(key), "Nearby-PC discovery carries the validated browser address for the PC picker");
        // Real UDP request/response on an isolated group/port: never advertise production DNS names on UDP 5353.
        using UdpClient reserve = new(new IPEndPoint(IPAddress.Loopback, 0));
        int isolatedPort = ((IPEndPoint)reserve.Client.LocalEndPoint!).Port;
        reserve.Close();
        using LocalNameService service = new(Loopback, key, new IPEndPoint(IPAddress.Parse("239.255.45.19"), isolatedPort));
        service.Start();
        await Wait(() => service.Url == "http://share-wollvo.local", 8);
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        byte[] query = LocalDns.Probe(LocalNameClaim.SharedHost, IPAddress.Loopback);
        query[0] = 0x12;
        query[1] = 0x34;
        await client.SendAsync(query, new IPEndPoint(IPAddress.Loopback, isolatedPort));
        UdpReceiveResult received = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(4));
        LocalDnsMessage reply = LocalDns.Parse(received.Buffer);
        Check(reply.Id == 0x1234 && reply.Response && reply.Addresses.Any(a => a.Name == LocalNameClaim.SharedHost && a.Address.Equals(IPAddress.Loopback)), "The actual responder answers local-computer unicast DNS requests with the correct transaction and address");
    }
    private static async Task NoteCasesAsync()
    {
        MetadataStore store = Store("Shared text checks");
        SharedNotes notes = new(store.Root);
        SharedNote first = notes.Add("https://example.com/family?q=hello", "This PC");
        Check(first.Kind == "link" && new SharedNotes(store.Root).Snapshot().Notes.Single().Content == first.Content, "Shared links persist locally and reopen in the dedicated board");
        string revision = notes.Snapshot().Revision;
        Check(notes.Snapshot(revision).Notes.Length == 0, "Idle note polling sends no repeated text bodies");
        SharedNote literal = notes.Add("<img src=x onerror=alert(1)>\nA plain note", "Family PC");
        Check(literal.Kind == "text" && notes.Snapshot().Notes[0].Content.StartsWith("<img", StringComparison.Ordinal), "Received markup is retained as plain text without execution");
        bool oversized = false;
        try
        {
            notes.Add(new string('a', 16001), "PC");
        }
        catch (InvalidDataException) { oversized = true; }
        Check(oversized && notes.Add("javascript:alert(1)", "PC").Kind == "text", "Oversized notes are rejected and executable URL schemes remain plain text");
        for (int i = 0; i < 105; i++)
            notes.Add("Retained note " + i, "This PC");
        Check(notes.Snapshot().Notes.Length == 100 && new FileInfo(Path.Combine(store.Root, "shared-notes.json")).Length <= 2 * 1024 * 1024, "The note history is bounded by both entry count and disk/serialization budget");
        using BrowserPortal portal = new(store, new(store), new(), true);
        await portal.StartAsync(Loopback, 0);
        using HttpClient browser = BrowserClient(portal.Url);
        Check((await browser.GetAsync("/api/notes")).StatusCode == HttpStatusCode.Unauthorized, "Notes require a connected browser session");
        await ConnectBrowser(browser);
        HttpResponseMessage saved = await browser.PostAsJsonAsync("/api/notes", new
        {
            content = "Phone note 😀\nhttps://example.com"
        });
        Check(saved.IsSuccessStatusCode && portal.Notes.Snapshot().Notes[0].Content.Contains("Phone note 😀", StringComparison.Ordinal), "Real browser POST saves Unicode text into the app's note store");
        using HttpRequestMessage wrong = new(HttpMethod.Post, "/api/notes")
        {
            Content = JsonContent.Create(new
            {
                content = "Must not save"
            })
        };
        wrong.Headers.Add("Origin", "http://attacker.invalid");
        Check((await browser.SendAsync(wrong)).StatusCode == HttpStatusCode.Forbidden && portal.Notes.Snapshot().Notes.All(n => n.Content != "Must not save"), "Cross-origin text submissions are rejected before persistence");
        Check((await browser.GetStringAsync("/api/notes")).Contains("Phone note", StringComparison.Ordinal), "The browser can read notes added to the dedicated app board");
    }
    private static async Task EfficientSyncCasesAsync()
    {
        string folder = Path.Combine(root, "Efficient sync");
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 500; i++)
            await File.WriteAllTextAsync(Path.Combine(folder, $"file-{i:000}.txt"), "Original bytes");
        DateTime clock = DateTime.UtcNow;
        using SyncScanner scanner = new(() => clock);
        Manifest first = await scanner.ScanAsync(folder, default);
        ScanStatistics warm = scanner.Statistics;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < 20; i++)
            await scanner.ScanAsync(folder, default);
        long idleAllocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        ScanStatistics idle = scanner.Statistics;
        Check(idle.HashedBytes == warm.HashedBytes && idle.MetadataScans == warm.MetadataScans && idle.CacheHits >= 20, "Twenty idle sync scans reuse the snapshot without re-reading files or enumerating the folder");
        Check(idleAllocated < 2 * 1024 * 1024, "Idle snapshot checks allocate less than 2 MB across twenty scans of a 500-file folder");
        string edited = Path.Combine(folder, "file-001.txt");
        DateTime modified = File.GetLastWriteTimeUtc(edited);
        await File.WriteAllTextAsync(edited, "Changed! bytes");
        File.SetLastWriteTimeUtc(edited, modified);
        await Task.Delay(250);
        Manifest changed = await scanner.ScanAsync(folder, default);
        Check(changed.Entries.Single(e => e.RelativePath == "file-001.txt").Sha256 != first.Entries.Single(e => e.RelativePath == "file-001.txt").Sha256, "Filesystem notifications detect same-size edits even when the timestamp is restored");
        clock = clock.AddMinutes(2);
        long bytesBeforeSweep = scanner.Statistics.HashedBytes;
        await scanner.ScanAsync(folder, default);
        Check(scanner.Statistics.HashedBytes == bytesBeforeSweep, "A periodic metadata sweep checks for missed changes without re-hashing unchanged content");
        clock = clock.AddMinutes(31);
        long hashesBeforeAudit = scanner.Statistics.HashedFiles;
        await scanner.ScanAsync(folder, default);
        Check(scanner.Statistics.HashedFiles - hashesBeforeAudit <= 1, "Periodic integrity auditing is spread across checks instead of re-reading the whole folder in a burst");
        SyncStore state = new(root);
        SyncLink link = new(Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid().ToString("N"), "Large baseline", folder)
        {
            Baseline = Enumerable.Range(0, 10000).ToDictionary(i => $"file-{i}.txt", _ => new string('A', 64))
        };
        state.SaveLink(link);
        long guardsBefore = GC.GetAllocatedBytesForCurrentThread();
        bool enabled = true;
        for (int i = 0; i < 20000; i++)
            enabled &= state.IsEnabled(link.Id, folder);
        long guardBytes = GC.GetAllocatedBytesForCurrentThread() - guardsBefore;
        Check(enabled && guardBytes < 100000, "Twenty thousand transfer guards do not clone a 10,000-file checksum baseline");
        File.WriteAllText(Path.Combine(root, "sync-performance.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            files = 500,
            idleScans = 20,
            idleAllocated,
            additionalIdleHashBytes = idle.HashedBytes - warm.HashedBytes,
            guardChecks = 20000,
            guardBytes
        }));
    }
}
