using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static int passed;
    private static string root = "";
    private static string? existingLargeSource;
    private static readonly List<string> skipped = [];
    private static void Check(bool condition, string text)
    {
        if (!condition)
            throw new Exception("FAIL " + text);
        passed++;
        Console.WriteLine("PASS " + text);
    }
    private static async Task Wait(Func<bool> condition, int seconds = 15)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds)
                throw new TimeoutException("Test condition timed out.");
            await Task.Delay(20);
        }
    }
    private static async Task<TransferRecord> Finished(TransferEngine engine, string id, string direction = "Sent", int seconds = 30)
    {
        await Wait(() => engine.Transfers.Records.Any(r => r.Id == id && r.Direction == direction && r.Terminal), seconds);
        return engine.Transfers.Records.First(r => r.Id == id && r.Direction == direction);
    }
    private static MetadataStore Store(string name)
    {
        string directory = Path.Combine(root, name);
        MetadataStore store = new(directory);
        store.SetPreferences(new Preferences { DeviceName = name, ReceiveFolder = Path.Combine(directory, "inbox") });
        return store;
    }
    private static NetworkAdapter Loopback => new("Loopback test", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), false);
    public static async Task<int> Main(string[] args)
    {
        root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Environment.CurrentDirectory, "work", "lantoss", "verification-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        int sourceOption = Array.IndexOf(args, "--large-source");
        if (sourceOption >= 0 && sourceOption + 1 < args.Length)
            existingLargeSource = Path.GetFullPath(args[sourceOption + 1]);
        try
        {
            if (args.Contains("--serve-browser"))
            {
                await BrowserPreviewServerAsync();
                return 0;
            }
            await RunAsync(args.Contains("--large"));
            Console.WriteLine($"\n{passed} checks passed. {skipped.Count} checks skipped.");
            foreach (string reason in skipped)
                Console.WriteLine("SKIP " + reason);
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                passed,
                skipped,
                finishedUtc = DateTime.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(root, "failure.txt"), error.ToString()); return 1; }
    }
    private static async Task RunAsync(bool large)
    {
        AppearanceCases();
        await FirewallCasesAsync();
        foreach (string invalid in new[] { "../escape", "/absolute", "a/../../escape", "a\\b", "a:b", "CON.txt", "COM¹.txt", "NUL", "space ", "bad.", "a//b", ".flowshare-hidden" })
        {
            bool rejected = false;
            try
            {
                FileCatalog.ValidateRelative(invalid);
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Unsafe path rejected: " + invalid);
        }
        FileCatalog.ValidateRelative("Family/परिवार 😀.txt");
        Check(true, "Unicode relative paths are accepted");
        PairingCode bootstrap = new();
        string old = bootstrap.Value;
        Check(bootstrap.Consume(old) && !bootstrap.Consume(old) && bootstrap.Value != old, "Pairing codes are single-use and rotate");
        Check(!bootstrap.Consume("wrong"), "Incorrect pairing code is rejected");
        using Identity a = Identity.Ephemeral();
        using Identity b = Identity.Ephemeral();
        using Identity c = Identity.Ephemeral();
        Check(a.Fingerprint != b.Fingerprint && a.Certificate.HasPrivateKey, "Independent device identities have distinct key fingerprints");
        Check(Identity.Comparison(a.Fingerprint, b.Fingerprint, "nonce") == Identity.Comparison(b.Fingerprint, a.Fingerprint, "nonce"), "Both devices derive the same comparison code");
        byte[] advertisement = MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("192.168.1.2"), a.Fingerprint, "A's PC", 48585);
        Peer? found = MdnsDiscovery.ParseAnnouncement(advertisement, IPAddress.Parse("192.168.1.2"));
        Check(found is { Name: "A's PC", Port: 48585, Paired: false } && found.Fingerprint == a.Fingerprint, "mDNS packet round-trip discovers an unverified Lantoss peer");
        Check(found?.SupportsQuickPair == true, "Discovery advertises connection without address/code entry");
        Check(MdnsDiscovery.ParseAnnouncement(MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("192.168.1.2"), a.Fingerprint, "Older PC", 48585, quickPair: false), IPAddress.Parse("192.168.1.2"))?.SupportsQuickPair == false, "Older peers retain the manual fallback without claiming quick connection");
        string unicodeDeviceName = new('界', 64);
        Check(MdnsDiscovery.ParseAnnouncement(MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("192.168.1.2"), a.Fingerprint, unicodeDeviceName, 48585), IPAddress.Parse("192.168.1.2"))?.Name == unicodeDeviceName, "mDNS preserves a maximum-length Unicode device name");
        Check(MdnsDiscovery.ParseAnnouncement(advertisement, IPAddress.Parse("192.168.1.3")) == null, "mDNS cannot redirect discovery to a different source IP");
        Check(MdnsDiscovery.ParseAnnouncement(MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("192.168.1.2"), a.Fingerprint, "A", 48585, 0), IPAddress.Parse("192.168.1.2")) == null, "mDNS goodbye records do not create online peers");
        foreach (byte[] packet in new[] { new byte[2], advertisement[..20], new byte[5000] })
        {
            bool safe = true;
            try
            {
                _ = MdnsDiscovery.ParseAnnouncement(packet, IPAddress.Loopback);
            }
            catch (InvalidDataException) { }
            catch { safe = false; }
            Check(safe, "Truncated/oversized DNS packet is safely rejected");
        }
        MetadataStore sa = Store("Sender"), sb = Store("Receiver");
        Check(new MetadataStore(sa.Root).Preferences.DeviceName == "Sender", "Preferences survive reopening metadata");
        await SlowStorageAsync();
        await NearbyPairingAsync(a, b, c);
        await DirectConnectionAsync(a, b);
        await BrowserCasesAsync();
        await SharedSpaceCasesAsync();
        await NativeSyncCasesAsync();
        await DiscoveryRecoveryCasesAsync();
        using TransferEngine ea = new(sa, a, true), eb = new(sb, b, true);
        string? leftCode = null, rightCode = null;
        ea.ApprovePair = (request, _) => { leftCode = request.ComparisonCode; return Task.FromResult(true); };
        eb.ApprovePair = (request, _) => { rightCode = request.ComparisonCode; return Task.FromResult(true); };
        ea.Start(Loopback, 0, false);
        eb.Start(Loopback, 0, false);
        bool wrongDenied = false;
        try
        {
            await ea.PairAsync(eb.Endpoint, "wrong", (_, _) => Task.FromResult(true));
        }
        catch (ProtocolException) { wrongDenied = true; }
        Check(wrongDenied && sa.Peers.Length == 0 && sb.Peers.Length == 0, "Incorrect bootstrap code grants no trust");
        eb.ApprovePair = (_, _) => Task.FromResult(false);
        bool declineDenied = false;
        try
        {
            await ea.PairAsync(eb.Endpoint, eb.PairingCode.Value, (_, _) => Task.FromResult(true));
        }
        catch (ProtocolException) { declineDenied = true; }
        Check(declineDenied && sa.Peers.Length == 0 && sb.Peers.Length == 0, "Declined pairing grants no file access");
        eb.ApprovePair = (request, _) => { rightCode = request.ComparisonCode; return Task.FromResult(true); };
        Peer recipient = await ea.PairAsync(eb.Endpoint, eb.PairingCode.Value, (request, _) => { leftCode = request.ComparisonCode; return Task.FromResult(true); });
        Check(leftCode != null && leftCode == rightCode && sa.Trusted(b.Fingerprint) != null && sb.Trusted(a.Fingerprint) != null, "Real mutual TLS pairing requires both comparison approvals");
        Check(sb.Peers[0].Port == ea.Port && new MetadataStore(sa.Root).Trusted(b.Fingerprint) != null, "Pairing stores the actual receive port and persistent public-key trust");
        int existingPort = ea.Port;
        sa.SetPreferences(sa.Preferences with
        {
            Discoverable = false
        });
        ea.ApplySavedPreferences();
        Check(ea.Running && ea.Port == existingPort, "Applying saved preferences preserves the existing receive listener");
        int approvals = 0;
        DuplicatePolicy policy = DuplicatePolicy.KeepBoth;
        eb.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(true, request.DefaultFolder, policy)); };
        string source = Path.Combine(root, "Family");
        Directory.CreateDirectory(Path.Combine(source, "Empty folder"));
        Directory.CreateDirectory(Path.Combine(source, "Nested"));
        await File.WriteAllTextAsync(Path.Combine(source, "Nested", "परिवार 😀.txt"), "Hello from Lantoss. नमस्ते!\n");
        await File.WriteAllBytesAsync(Path.Combine(source, "empty.txt"), []);
        byte[] bytes = RandomNumberGenerator.GetBytes(1024 * 1024);
        await File.WriteAllBytesAsync(Path.Combine(source, "binary.bin"), bytes);
        TransferRecord initial = ea.Send(recipient, [source]);
        TransferRecord complete = await Finished(ea, initial.Id);
        if (complete.State != TransferState.Completed)
            throw new Exception("Transfer failed: " + complete.Error);
        await Finished(eb, initial.Id, "Received");
        string destination = Path.Combine(sb.Preferences.ReceiveFolder, "Family");
        Check(approvals == 1 && complete.VerifiedFiles == 3, "Receiver approval precedes a verified multiple-file transfer");
        Check(File.ReadAllBytes(Path.Combine(destination, "binary.bin")).SequenceEqual(bytes), "Binary file bytes are preserved over mutual TLS");
        Check(File.ReadAllText(Path.Combine(destination, "Nested", "परिवार 😀.txt")) == File.ReadAllText(Path.Combine(source, "Nested", "परिवार 😀.txt")), "Nested Unicode filename and contents are preserved");
        Check(new FileInfo(Path.Combine(destination, "empty.txt")).Length == 0 && Directory.Exists(Path.Combine(destination, "Empty folder")), "Empty files and empty folders survive transfer");
        Check(complete.TransferredBytes == complete.TotalBytes && complete.Progress == 100, "Progress counts actual bytes and completes after acknowledgments");
        TransferRecord duplicate = ea.Send(recipient, [source]);
        Check((await Finished(ea, duplicate.Id)).State == TransferState.Completed && Directory.Exists(Path.Combine(sb.Preferences.ReceiveFolder, "Family (1)")), "Keep both preserves the original folder and adds a numbered copy");
        policy = DuplicatePolicy.Skip;
        TransferRecord skip = await Finished(ea, ea.Send(recipient, [source]).Id);
        Check(skip.State == TransferState.Completed && skip.SkippedFiles == 3 && skip.TransferredBytes == 0, "Skip does not overwrite or retransmit existing files");
        string replacement = "Replacement chosen explicitly";
        await File.WriteAllTextAsync(Path.Combine(source, "empty.txt"), replacement);
        policy = DuplicatePolicy.Replace;
        TransferRecord replace = await Finished(ea, ea.Send(recipient, [source]).Id);
        Check(replace.State == TransferState.Completed && File.ReadAllText(Path.Combine(destination, "empty.txt")) == replacement, "Explicit Replace atomically saves verified replacement bytes");
        eb.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(false, request.DefaultFolder));
        TransferRecord declined = await Finished(ea, ea.Send(recipient, [source]).Id);
        Check(declined.State == TransferState.Declined && !Directory.Exists(Path.Combine(sb.Preferences.ReceiveFolder, "Family (2)")), "Declined transfers do not create destination files");
        eb.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(true, request.DefaultFolder));
        TransferRecord retried = await Finished(ea, ea.Retry(declined).Id);
        Check(retried.State == TransferState.Completed && retried.Id != declined.Id, "Retry creates a new verified job and requests recipient approval again");
        using TransferEngine ec = new(Store("Unpaired"), c, true);
        MetadataStore sc = Store("Attacker");
        sc.UpsertPeer(recipient);
        using TransferEngine attacker = new(sc, c, true);
        TransferRecord unauthorized = await Finished(attacker, attacker.Send(recipient, [Path.Combine(source, "empty.txt")]).Id);
        Check(unauthorized.State == TransferState.Failed && sb.Trusted(c.Fingerprint) == null, "A device that trusts the receiver cannot send without reciprocal pairing");
        await RawCasesAsync(eb, a, b, sb);
        string changed = Path.Combine(root, "changed.txt");
        await File.WriteAllTextAsync(changed, "before");
        eb.ApproveReceive = async (request, _) => { await File.WriteAllTextAsync(changed, "modified while awaiting approval"); return new(true, request.DefaultFolder); };
        TransferRecord sourceChanged = await Finished(ea, ea.Send(recipient, [changed]).Id);
        Check(sourceChanged.State == TransferState.Failed && sourceChanged.Error.Contains("changed", StringComparison.OrdinalIgnoreCase), "Changed source is detected before bytes are sent");
        eb.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(true, request.DefaultFolder));
        Peer fake = recipient with
        {
            Fingerprint = c.Fingerprint,
            Name = "Mismatched key"
        };
        sa.UpsertPeer(fake);
        TransferRecord mismatch = await Finished(ea, ea.Send(fake, [Path.Combine(source, "empty.txt")]).Id);
        Check(mismatch.State == TransferState.Failed, "Saved public-key pin rejects a different TLS identity at the same address");
        sa.ForgetPeer(fake.Fingerprint, false);
        MetadataStore restart = Store("Restart");
        restart.SetHistory([new() { Id = Guid.NewGuid().ToString("N"), Direction = "Sent", PeerFingerprint = b.Fingerprint, PeerName = "B", State = TransferState.Sending }]);
        Check(new MetadataStore(restart.Root).History[0].State == TransferState.Failed, "Restart marks unfinished jobs interrupted rather than complete");
        try
        {
            string link = Path.Combine(root, "Link");
            Directory.CreateSymbolicLink(link, source);
            bool rejected = false;
            try
            {
                await FileCatalog.BuildAsync([link], Guid.NewGuid().ToString("N"), false, default);
            }
            catch (IOException) { rejected = true; }
            Check(rejected, "Source symbolic-link traversal is rejected");
            bool receiveRejected = false;
            try
            {
                _ = new ReceivePlan(await FileCatalog.BuildAsync([source], Guid.NewGuid().ToString("N"), true, default), link, DuplicatePolicy.KeepBoth);
            }
            catch (IOException) { receiveRejected = true; }
            Check(receiveRejected, "Receive-folder symbolic-link escape is rejected");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { skipped.Add("Symbolic-link creation unavailable: " + error.Message); }
        if (large)
            await VolumeCasesAsync(ea, eb, recipient, sb);
        eb.Forget(sb.Peers.First(p => p.Fingerprint == a.Fingerprint), true);
        Check(sb.Blocked(a.Fingerprint) && sb.Trusted(a.Fingerprint) == null, "Blocking a device revokes its verified identity");
        TransferRecord blocked = await Finished(ea, ea.Send(recipient, [Path.Combine(source, "empty.txt")]).Id);
        Check(blocked.State == TransferState.Failed, "Blocked peer cannot open future file transfers");
        int countBeforeClear = Directory.GetFiles(sb.Preferences.ReceiveFolder, "*", SearchOption.AllDirectories).Length;
        eb.Transfers.ClearFinished();
        Check(eb.Transfers.Records.All(r => !r.Terminal) && Directory.GetFiles(sb.Preferences.ReceiveFolder, "*", SearchOption.AllDirectories).Length == countBeforeClear, "Clearing history removes metadata and preserves received files");
        using CancellationTokenSource closing = new(TimeSpan.FromSeconds(10));
        await ea.ShutdownAsync(closing.Token);
        await eb.ShutdownAsync(closing.Token);
        Check(new MetadataStore(sa.Root).History.All(r => r.Terminal), "Graceful shutdown drains history writes before reopening");
    }
    private static async Task NearbyPairingAsync(Identity first, Identity second, Identity different)
    {
        MetadataStore left = Store("Quick sender"), right = Store("Quick receiver");
        Check(left.Preferences.StartReceivingOnOpen, "Automatic nearby startup is enabled by default");
        left.SetPreferences(left.Preferences with
        {
            StartReceivingOnOpen = false
        });
        Check(!new MetadataStore(left.Root).Preferences.StartReceivingOnOpen, "Automatic nearby startup can be disabled and stays disabled after restart");
        using TransferEngine sender = new(left, first, true), receiver = new(right, second, true);
        sender.Start(Loopback, 0, false);
        receiver.Start(Loopback, 0, false);
        Peer nearby = new()
        {
            Fingerprint = second.Fingerprint,
            Name = "Quick receiver",
            Address = "127.0.0.1",
            Port = receiver.Port,
            SupportsQuickPair = true
        };
        int receiverPrompts = 0, senderPrompts = 0;
        string? incomingCode = null, outgoingCode = null;
        receiver.ApprovePair = (request, _) => { receiverPrompts++; incomingCode = request.ComparisonCode; return Task.FromResult(false); };
        async Task<bool> Rejected(Func<Task<Peer>> connect)
        {
            try
            {
                await connect();
                return false;
            }
            catch (Exception error) when (error is ProtocolException or AuthenticationException or IOException) { return true; }
        }
        Check(await Rejected(() => sender.PairNearbyAsync(nearby, (_, _) => { senderPrompts++; return Task.FromResult(true); })) && left.Peers.Length == 0 && right.Peers.Length == 0,
            "Declining a nearby connection grants neither PC access");
        await Task.Delay(50);
        receiver.ApprovePair = (request, _) => { receiverPrompts++; incomingCode = request.ComparisonCode; return Task.FromResult(true); };
        Check(await Rejected(() => sender.PairNearbyAsync(nearby, (_, _) => Task.FromResult(false))) && left.Peers.Length == 0 && right.Peers.Length == 0,
            "Nearby pairing also requires the initiating PC's approval");
        await Task.Delay(50);
        int beforeIncoming = receiverPrompts, beforeOutgoing = senderPrompts;
        Check(await Rejected(() => sender.PairNearbyAsync(nearby with { Fingerprint = different.Fingerprint }, (_, _) => { senderPrompts++; return Task.FromResult(true); })) && receiverPrompts == beforeIncoming && senderPrompts == beforeOutgoing,
            "A mismatched discovered public key is rejected before approval prompts");
        Peer paired = await sender.PairNearbyAsync(nearby, (request, _) => { senderPrompts++; outgoingCode = request.ComparisonCode; return Task.FromResult(true); });
        Check(incomingCode != null && incomingCode == outgoingCode && left.Trusted(second.Fingerprint) != null && right.Trusted(first.Fingerprint) != null,
            "Nearby pairing needs no typed code and binds matching approvals to both TLS keys");
        Check(new MetadataStore(left.Root).Trusted(second.Fingerprint)?.SupportsQuickPair == true && new MetadataStore(right.Root).Trusted(first.Fingerprint) != null,
            "Approved nearby PCs are remembered in persistent trust metadata");
        string source = Path.Combine(left.Root, "quick-transfer.txt");
        await File.WriteAllTextAsync(source, "Connected without typing an address or password.");
        int fileApprovals = 0;
        receiver.ApproveReceive = (request, _) => { fileApprovals++; return Task.FromResult(new ReceiveDecision(true, request.DefaultFolder)); };
        TransferRecord sent = await Finished(sender, sender.Send(paired, [source]).Id);
        await Finished(receiver, sent.Id, "Received");
        Check(sent.State == TransferState.Completed && fileApprovals == 1 && File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(right.Preferences.ReceiveFolder, "quick-transfer.txt"))),
            "A quickly connected PC transfers exact bytes only after separate file approval");
        beforeIncoming = receiverPrompts;
        right.SetPreferences(right.Preferences with
        {
            Discoverable = false
        });
        Check(await Rejected(() => sender.PairNearbyAsync(nearby, (_, _) => Task.FromResult(true))) && receiverPrompts == beforeIncoming,
            "Hidden PCs reject code-free connection requests without prompting");
        right.SetPreferences(right.Preferences with
        {
            Discoverable = true
        });
        receiver.Forget(right.Trusted(first.Fingerprint)!, true);
        Check(await Rejected(() => sender.PairNearbyAsync(nearby, (_, _) => Task.FromResult(true))) && receiverPrompts == beforeIncoming,
            "Blocked PCs cannot use nearby pairing to regain access");
        using TransferEngine limited = new(Store("Rate-limited receiver"), different, true);
        limited.Start(Loopback, 0, false);
        int prompts = 0;
        limited.ApprovePair = (_, _) => { prompts++; return Task.FromResult(false); };
        Peer ratePeer = nearby with
        {
            Fingerprint = different.Fingerprint,
            Port = limited.Port
        };
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Rejected(() => sender.PairNearbyAsync(ratePeer, (_, _) => Task.FromResult(true)));
            await Task.Delay(50);
        }
        string rejection = "";
        try
        {
            await sender.PairNearbyAsync(ratePeer, (_, _) => Task.FromResult(true));
        }
        catch (ProtocolException error) { rejection = error.Message; }
        Check(prompts == 5 && rejection.Contains("Too many nearby", StringComparison.Ordinal), "Nearby approval prompts have a global five-per-minute limit");
        using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
        await sender.ShutdownAsync(shutdown.Token);
        await receiver.ShutdownAsync(shutdown.Token);
        await limited.ShutdownAsync(shutdown.Token);
    }
    private sealed class SlowWriter : MetadataStore.IAtomicWriter
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public void Write(string filename, byte[] bytes)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new IOException("Simulated storage timeout.");
            new MetadataStore.AtomicDiskWriter().Write(filename, bytes);
        }
    }
    private static async Task SlowStorageAsync()
    {
        SlowWriter disk = new();
        MetadataStore slow = new(Path.Combine(root, "Slow metadata"), disk);
        TransferRegistry registry = new(slow);
        TransferJob job = registry.Add(new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Direction = "Sent",
            PeerName = "Test",
            PeerFingerprint = new string('A', 64),
            State = TransferState.Queued
        });
        if (!await Task.Run(() => disk.Entered.Wait(TimeSpan.FromSeconds(2))))
            throw new TimeoutException("Slow metadata writer did not start.");
        Task responsive = Task.Run(() => { _ = registry.Records; _ = slow.Preferences; job.Cancel(); });
        Check(await Task.WhenAny(responsive, Task.Delay(1000)) == responsive, "Slow metadata storage does not block state reads or cancellation");
        disk.Release.Set();
        await registry.FlushAsync();
        Check(new MetadataStore(slow.Root).History.Single().State == TransferState.Cancelled, "Background metadata flush persists the latest cancelled state");
    }
    private static async Task<(TcpClient Client, Stream Stream)> RawAsync(TransferEngine receiver, Identity sender, Identity expected)
    {
        TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, receiver.Port);
        ManagedTls.Connection connection = await ManagedTls.ConnectAsync(client, sender, expected.Fingerprint, default);
        return (client, connection.Stream);
    }
    private static Manifest One(string name, long size, string hash) => new() { Id = Guid.NewGuid().ToString("N"), Entries = [new() { RelativePath = name, Size = size, Sha256 = hash }] };
    private static async Task RawCasesAsync(TransferEngine receiver, Identity sender, Identity expected, MetadataStore store)
    {
        foreach (string unsafePath in new[] { "../escape.txt", "CON.txt" })
        {
            var channel = await RawAsync(receiver, sender, expected);
            using (channel.Client)
            await using (channel.Stream)
            {
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "send",
                    Manifest = One(unsafePath, 0, Convert.ToHexString(SHA256.HashData([])))
                }, default);
                bool rejected = false;
                try
                {
                    await Wire.ReadAsync(channel.Stream, default);
                }
                catch (ProtocolException) { rejected = true; }
                Check(rejected, "Encrypted malicious manifest is rejected: " + unsafePath);
            }
        }
        {
            var channel = await RawAsync(receiver, sender, expected);
            using (channel.Client)
            await using (channel.Stream)
            {
                Manifest huge = One("oversized.bin", FileCatalog.MaxFileBytes, new string('0', 64));
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "send",
                    Manifest = huge
                }, default);
                bool rejected = false;
                try
                {
                    await Wire.ReadAsync(channel.Stream, default);
                }
                catch (ProtocolException error) { rejected = error.Message.Contains("space", StringComparison.OrdinalIgnoreCase); }
                Check(rejected && !File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "oversized.bin")), "Insufficient disk space is rejected before creating a large file");
            }
        }
        {
            var channel = await RawAsync(receiver, sender, expected);
            using (channel.Client)
            await using (channel.Stream)
            {
                Manifest corrupt = One("corrupt.bin", 4, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("good"))));
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "send",
                    Manifest = corrupt
                }, default);
                Wire.Require(await Wire.ReadAsync(channel.Stream, default), "accepted");
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "file",
                    Index = 0
                }, default);
                await channel.Stream.WriteAsync(Encoding.UTF8.GetBytes("evil"));
                bool rejected = false;
                try
                {
                    await Wire.ReadAsync(channel.Stream, default);
                }
                catch (ProtocolException error) { rejected = error.Message.Contains("Integrity", StringComparison.OrdinalIgnoreCase); }
                Check(rejected && !File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "corrupt.bin")), "Checksum mismatch never commits corrupted data");
                Check((await Finished(receiver, corrupt.Id, "Received")).State == TransferState.Failed, "Integrity failure is recorded as failed, never completed");
            }
        }
        {
            var channel = await RawAsync(receiver, sender, expected);
            Manifest partial = One("disconnected.bin", 1_000_000, new string('0', 64));
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "send",
                Manifest = partial
            }, default);
            Wire.Require(await Wire.ReadAsync(channel.Stream, default), "accepted");
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "file",
                Index = 0
            }, default);
            await channel.Stream.WriteAsync(new byte[65536]);
            await channel.Stream.DisposeAsync();
            channel.Client.Dispose();
            Check((await Finished(receiver, partial.Id, "Received")).State == TransferState.Failed, "Mid-file disconnect is recorded as failed");
            await Wait(() => !Directory.GetFiles(store.Preferences.ReceiveFolder, "*.part", SearchOption.AllDirectories).Any());
            Check(!File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "disconnected.bin")), "Interrupted transfer cleans its temporary file and leaves no final file");
        }
        {
            var channel = await RawAsync(receiver, sender, expected);
            using (channel.Client)
            await using (channel.Stream)
            {
                Manifest cancelled = One("cancelled.bin", 1_000_000, new string('0', 64));
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "send",
                    Manifest = cancelled
                }, default);
                Wire.Require(await Wire.ReadAsync(channel.Stream, default), "accepted");
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "file",
                    Index = 0
                }, default);
                await channel.Stream.WriteAsync(new byte[65536]);
                await Task.Delay(220);
                await channel.Stream.WriteAsync(new byte[65536]);
                await Wait(() => receiver.Transfers.Records.Any(r => r.Id == cancelled.Id && r.TransferredBytes > 0));
                receiver.Transfers.Cancel(cancelled.Id, "Received");
                Check((await Finished(receiver, cancelled.Id, "Received")).State == TransferState.Cancelled, "Receiver cancellation stops an active streaming job");
                await Wait(() => !Directory.GetFiles(store.Preferences.ReceiveFolder, "*.part", SearchOption.AllDirectories).Any());
                Check(!File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "cancelled.bin")), "Receiver cancellation preserves destination safety");
            }
        }
    }
    private static async Task VolumeCasesAsync(TransferEngine sender, TransferEngine receiver, Peer peer, MetadataStore destination)
    {
        string many = Path.Combine(root, "Many small files");
        Directory.CreateDirectory(many);
        byte[] small = RandomNumberGenerator.GetBytes(1024);
        for (int i = 0; i < 1000; i++)
            await File.WriteAllBytesAsync(Path.Combine(many, $"file-{i:0000}.bin"), small);
        Stopwatch watch = Stopwatch.StartNew();
        TransferRecord result = await Finished(sender, sender.Send(peer, [many]).Id, seconds: 180);
        Check(result.State == TransferState.Completed && result.VerifiedFiles == 1000, "1,000 small files transfer with individual integrity verification");
        Console.WriteLine($"MEASURE many-small-files: 1000 × 1024 B; end-to-end loopback {watch.Elapsed.TotalSeconds:0.00}s");
        long tenGb = 10L * 1024 * 1024 * 1024;
        if (FileCatalog.FreeSpace(root) < 2 * tenGb + 4L * 1024 * 1024 * 1024)
        {
            skipped.Add("10 GiB transfer needs at least 24 GiB free in the test workspace.");
            return;
        }
        string largeFile = existingLargeSource ?? Path.Combine(root, "10-GiB-test.bin");
        byte[] block = RandomNumberGenerator.GetBytes(1024 * 1024);
        if (existingLargeSource == null)
        {
            await using FileStream file = new(largeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, block.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            for (long written = 0; written < tenGb; written += block.Length)
                await file.WriteAsync(block);
        }
        if (new FileInfo(largeFile).Length != tenGb)
            throw new InvalidDataException("The reusable test source must be exactly 10 GiB.");
        watch.Restart();
        TransferRecord largeJob = sender.Send(peer, [largeFile]);
        using CancellationTokenSource monitor = new();
        Task monitoring = Task.Run(async () =>
        {
            try
            {
                while (!monitor.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), monitor.Token);
                    TransferRecord? snapshot = sender.Transfers.Records.FirstOrDefault(r => r.Id == largeJob.Id);
                    Console.WriteLine($"LARGE {watch.Elapsed.TotalSeconds:0}s · {snapshot?.State} · {snapshot?.TransferredBytes ?? 0}/{tenGb} B · process working set {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB");
                }
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            result = await Finished(sender, largeJob.Id, seconds: 600);
        }
        finally { monitor.Cancel(); await monitoring; }
        if (result.State != TransferState.Completed)
            Console.WriteLine("LARGE ERROR: " + result.Error);
        Check(result.State == TransferState.Completed && result.TotalBytes == tenGb, "10 GiB file completes with sender/receiver SHA-256 verification");
        string received = Path.Combine(destination.Preferences.ReceiveFolder, Path.GetFileName(largeFile));
        await using (FileStream original = File.OpenRead(largeFile))
        await using (FileStream final = File.OpenRead(received))
        {
            byte[] originalHash = await SHA256.HashDataAsync(original);
            byte[] finalHash = await SHA256.HashDataAsync(final);
            Check(originalHash.SequenceEqual(finalHash), "Independent destination checksum matches the 10 GiB original");
        }
        Console.WriteLine($"MEASURE large-file: {tenGb} B; end-to-end loopback including preparation and verification {watch.Elapsed.TotalSeconds:0.00}s");
        if (existingLargeSource == null)
            File.Delete(largeFile);
        File.Delete(received);
        Check((existingLargeSource != null || !File.Exists(largeFile)) && !File.Exists(received), "Generated large test artifacts are cleaned; a supplied source is kept");
    }
}
