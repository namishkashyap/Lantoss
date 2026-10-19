using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Lantoss.Core;

namespace Lantoss.Networking;

/// <summary>
/// Coordinates discovery, pinned TLS, pairing and approved native transfers. Partial files add reconnection, shared-space operations and folder synchronization.
/// </summary>
public sealed partial class TransferEngine : IDisposable
{
    private readonly MetadataStore store;
    private readonly Identity identity;
    private readonly bool allowLoopback;
    private readonly bool automaticSync;
    private readonly SemaphoreSlim connections = new(8, 8), sending = new(2, 2), receiving = new(2, 2), pairing = new(1, 1);
    private readonly ConcurrentDictionary<TcpClient, string> sockets = new();
    private readonly ConcurrentDictionary<string, Peer> nearby = new();
    private readonly ConcurrentDictionary<long, Task> workers = new();
    private long nextWorker;
    private Task acceptLoop = Task.CompletedTask;
    private readonly Dictionary<string, (int Count, DateTime Start)> attempts = [];
    private readonly Queue<DateTime> nearbyPairAttempts = new();
    private readonly object gate = new();
    private TcpListener? listener; private CancellationTokenSource? lifetime; private MdnsDiscovery? discovery;
    private int disposed;
    public TransferRegistry Transfers
    {
        get;
    }
    public PairingCode PairingCode { get; } = new();
    public BrowserPortal Browser
    {
        get;
    }
    public Func<PairRequest, CancellationToken, Task<bool>>? ApprovePair
    {
        get; set;
    }
    public Func<ReceiveRequest, CancellationToken, Task<ReceiveDecision>>? ApproveReceive
    {
        get; set;
    }
    public event Action? PeersChanged;
    public event Action<string>? Status;
    public event Action<TransferRecord>? Completed;
    public event Action<Exception>? Diagnostic;
    public NetworkAdapter? Adapter
    {
        get; private set;
    }
    public int Port
    {
        get; private set;
    }
    public string Endpoint => Adapter == null ? "Not receiving" : $"{Adapter.Address}:{Port}";
    public bool Running => listener != null;
    public string Fingerprint => identity.Fingerprint;
    public string DiscoveryStatus { get; private set; } = "Start receiving to discover nearby PCs.";
    public Peer[] Peers
    {
        get
        {
            List<Peer> list = store.Peers.ToList();
            foreach (Peer candidate in nearby.Values.Where(p => p.Online))
            {
                int index = list.FindIndex(p => p.Fingerprint == candidate.Fingerprint);
                if (index < 0)
                    list.Add(candidate);
                // Advertisements are not proof that a saved identity is present.
                // Saved peers become online only after an authenticated probe.
            }
            return list.Where(p => p.Fingerprint != Fingerprint).OrderByDescending(p => p.Favorite).ThenByDescending(p => p.Online).ThenBy(p => p.Name).ToArray();
        }
    }
    public TransferEngine(MetadataStore store, Identity identity, bool allowLoopback = false, bool automaticSync = true)
    {
        this.store = store;
        this.identity = identity;
        this.allowLoopback = allowLoopback;
        this.automaticSync = automaticSync;
        Transfers = new(store);
        Browser = new(store, Transfers, PairingCode, allowLoopback);
        Sync = new SyncStore(store.Root);
        Browser.ApproveReceive = (request, token) => ApproveReceive?.Invoke(request, token) ?? Task.FromResult(new ReceiveDecision(false, request.DefaultFolder));
        Transfers.Changed += r => { if (r.State == TransferState.Completed) Completed?.Invoke(r); };
    }
    public void Start(NetworkAdapter adapter, int requestedPort = 48585, bool advertise = true)
    {
        if (Running)
            throw new InvalidOperationException("Receiving is already on.");
        if (!NetworkAdapter.Private(adapter.Address) && !(allowLoopback && IPAddress.IsLoopback(adapter.Address)))
            throw new InvalidOperationException("Choose a private IPv4 home or workplace network.");
        FileCatalog.CheckSafeDirectory(store.Preferences.ReceiveFolder);
        TcpListener candidate = new(IPAddress.IsLoopback(adapter.Address) ? adapter.Address : IPAddress.Any, requestedPort)
        {
            ExclusiveAddressUse = true
        };
        candidate.Start(8);
        lifetime = new();
        listener = candidate;
        Adapter = adapter;
        receiveNetworks = IPAddress.IsLoopback(adapter.Address) ? [adapter] : NetworkAdapter.Available().Append(adapter).DistinctBy(network => network.Address).ToArray();
        Port = ((IPEndPoint)candidate.LocalEndpoint).Port;
        CancellationToken receiverToken = lifetime.Token;
        acceptLoop = Task.Run(() => AcceptAsync(candidate, receiverToken));
        if (advertise && !IPAddress.IsLoopback(adapter.Address))
        {
            try
            {
                discovery = new MdnsDiscovery(adapter, identity.Fingerprint, store.Preferences.DeviceName, Port, store.Preferences.Discoverable, store.Preferences.RequireConnectionCode);
                discovery.Found += Observe;
                discovery.Start();
                DiscoveryStatus = store.Preferences.Discoverable ? "mDNS discovery is on for this network." : "Listening for peers; this PC is hidden from discovery.";
            }
            catch (Exception error) when (error is SocketException or IOException)
            {
                discovery?.Dispose();
                discovery = null;
                DiscoveryStatus = "Discovery unavailable. Pair using an address instead. " + error.Message;
            }
        }
        Status?.Invoke("Receiving is on. Approvals are required for every transfer.");
        StartLanDiscovery();
        ApplyBrowserSettings();
        RefreshDiscovery();
        RunWorker(() => MaintainConnectionsAsync(receiverToken));
        if (automaticSync)
            RunWorker(() => SyncLoopAsync(receiverToken));
        PeersChanged?.Invoke();
    }
    public void Stop()
    {
        Browser.Stop();
        lifetime?.Cancel();
        listener?.Stop();
        listener = null;
        discovery?.Dispose();
        discovery = null;
        lanDiscovery?.Dispose();
        lanDiscovery = null;
        foreach (TcpClient socket in sockets.Keys)
            socket.Dispose();
        Transfers.CancelAll();
        Adapter = null;
        Port = 0;
        nearby.Clear();
        DiscoveryStatus = "Receiving is off. Manual outgoing connections are still available.";
        Status?.Invoke("Receiving stopped. Active connections closed.");
        PeersChanged?.Invoke();
    }
    public async Task ShutdownAsync(CancellationToken token)
    {
        Stop();
        await acceptLoop.WaitAsync(token);
        while (true)
        {
            Task[] active = workers.Values.Where(task => !task.IsCompleted).ToArray();
            if (active.Length == 0)
                break;
            await Task.WhenAll(active).WaitAsync(token);
        }
        await Browser.WhenStopped.WaitAsync(token);
        await Transfers.FlushAsync(token);
    }
    private void RunWorker(Func<Task> operation)
    {
        long id = Interlocked.Increment(ref nextWorker);
        Task task = Task.Run(operation);
        workers[id] = task;
        _ = task.ContinueWith(finished => { workers.TryRemove(id, out _); if (finished.Exception != null) Diagnostic?.Invoke(finished.Exception); }, TaskScheduler.Default);
    }
    public void RefreshDiscovery()
    {
        discovery?.Query();
        lanDiscovery?.QueryNow();
        foreach (Peer peer in store.Peers.Where(p => p.Paired && !p.Blocked))
            ProbeInBackground(peer);
        PeersChanged?.Invoke();
    }
    public void ApplySavedPreferences()
    {
        ApplyBrowserSettings();
        discovery?.Dispose();
        discovery = null;
        if (!Running || Adapter == null || IPAddress.IsLoopback(Adapter.Address))
            return;
        try
        {
            discovery = new MdnsDiscovery(Adapter, Fingerprint, store.Preferences.DeviceName, Port, store.Preferences.Discoverable, store.Preferences.RequireConnectionCode);
            discovery.Found += Observe;
            discovery.Start();
            DiscoveryStatus = store.Preferences.Discoverable ? "mDNS discovery is on for this network." : "This PC is hidden from discovery. Manual pairing is available.";
        }
        catch (Exception error) when (error is SocketException or IOException)
        {
            discovery?.Dispose();
            discovery = null;
            DiscoveryStatus = "Discovery unavailable. Use manual pairing. " + error.Message;
        }
        StartLanDiscovery();
        PeersChanged?.Invoke();
    }
    private void ApplyBrowserSettings()
    {
        Browser.ApplySecurityPolicy();
        if (!Running || Adapter == null || !store.Preferences.BrowserSharingEnabled)
        {
            Browser.Stop();
            return;
        }
        if (Browser.Running)
            return;
        try
        {
            Browser.StartAsync(Adapter, allowLoopback ? 0 : 48586).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is IOException or SocketException or InvalidOperationException)
        {
            Status?.Invoke("Phone/browser sharing could not start: " + error.Message);
        }
    }
    internal void Observe(Peer peer)
    {
        if (peer.Fingerprint == Fingerprint || store.Blocked(peer.Fingerprint))
            return;
        if (peer.Fingerprint.Length != 64 || !peer.Fingerprint.All(char.IsAsciiHexDigit) || !IPAddress.TryParse(peer.Address, out IPAddress? source) || !receiveNetworks.Any(network => network.Contains(source)) || peer.Port is < 1 or > 65535)
            return;
        if (nearby.Count >= 128 && !nearby.ContainsKey(peer.Fingerprint))
            return;
        nearby[peer.Fingerprint] = peer;
        AutoConnectPeer(peer);
        PeersChanged?.Invoke();
        Peer? trusted = store.Trusted(peer.Fingerprint);
        if (trusted != null)
            ProbeInBackground(trusted with
            {
                Address = peer.Address,
                Port = peer.Port
            });
    }
    private async Task ProbeAsync(Peer peer)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
            await using Channel channel = await ConnectAsync(peer.Address, peer.Port, peer.Fingerprint, timeout.Token);
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "ping",
                Name = store.Preferences.DeviceName
            }, timeout.Token);
            Wire.Require(await Wire.ReadAsync(channel.Stream, timeout.Token), "pong");
            if (store.Trusted(peer.Fingerprint) != null)
                store.UpsertPeer(peer with
                {
                    LastSeenUtc = DateTime.UtcNow
                });
            PeersChanged?.Invoke();
        }
        catch (Exception error) when (error is IOException or SocketException or AuthenticationException or OperationCanceledException) { }
    }
    public void EditPeer(Peer peer)
    {
        store.UpsertPeer(peer);
        PeersChanged?.Invoke();
    }
    public void Forget(Peer peer, bool block)
    {
        Transfers.Revoke(peer.Fingerprint);
        foreach ((TcpClient socket, string key) in sockets)
        {
            if (key == peer.Fingerprint)
                socket.Dispose();
        }
        store.ForgetPeer(peer.Fingerprint, block);
        foreach (SyncGrant grant in Sync.Grants.Where(grant => grant.PeerKey == peer.Fingerprint))
            Sync.Revoke(grant.Id, peer.Fingerprint);
        foreach (SyncLink link in Sync.Links.Where(link => link.PeerKey == peer.Fingerprint && link.Enabled))
            PauseSync(link.Id, true);
        nearby.TryRemove(peer.Fingerprint, out _);
        PeersChanged?.Invoke();
    }
    public async Task<Peer> PairAsync(string endpoint, string code, Func<PairRequest, CancellationToken, Task<bool>> compare, CancellationToken token = default)
        => await PairCoreAsync(endpoint, code, null, compare, token, false);
    public async Task<Peer> ConnectDirectAsync(Peer peer, string code = "", CancellationToken token = default)
    {
        if (!peer.SupportsDirectConnect)
            throw new ProtocolException("Update Lantoss on both PCs for direct connection.");
        return await PairCoreAsync(peer.Endpoint, code, peer.Fingerprint, (_, _) => Task.FromResult(true), token, true);
    }
    public async Task<Peer> PairNearbyAsync(Peer peer, Func<PairRequest, CancellationToken, Task<bool>> compare, CancellationToken token = default)
    {
        if (!peer.SupportsQuickPair)
            throw new ProtocolException("Update Lantoss on both PCs to connect without typing, or use manual connection.");
        if (peer.Fingerprint.Length != 64 || !peer.Fingerprint.All(char.IsAsciiHexDigit))
            throw new ProtocolException("Refresh nearby PCs before connecting.");
        return await PairCoreAsync(peer.Endpoint, "", peer.Fingerprint, compare, token, false);
    }
    private async Task<Peer> PairCoreAsync(string endpoint, string code, string? expectedKey, Func<PairRequest, CancellationToken, Task<bool>> compare, CancellationToken token, bool direct)
    {
        (IPAddress address, int port) = NetworkAdapter.ParseEndpoint(endpoint, allowLoopback);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await using Channel channel = await ConnectAsync(address.ToString(), port, expectedKey, timeout.Token);
        if (channel.Fingerprint == Fingerprint)
            throw new InvalidDataException("This is this computer. Use the address shown on the other PC.");
        if (store.Blocked(channel.Fingerprint))
            throw new InvalidDataException("This device is blocked. Unblock it in Devices before pairing.");
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Wire.WriteAsync(channel.Stream, new()
        {
            Kind = direct ? "pair-direct" : expectedKey == null ? "pair" : "pair-nearby",
            Name = store.Preferences.DeviceName,
            Code = code,
            Nonce = nonce,
            ListenPort = Running ? Port : 48585
        }, timeout.Token);
        WireMessage challenge = await Wire.ReadAsync(channel.Stream, timeout.Token, 120);
        if (challenge.Kind == "code-required")
            throw new CodeRequiredException("This PC requires an access code. Enter the code shown in its Phone & browser screen.");
        Wire.Require(challenge, "pair-challenge");
        string comparison = Identity.Comparison(Fingerprint, channel.Fingerprint, nonce);
        if (challenge.Fingerprint != channel.Fingerprint || challenge.Comparison != comparison)
            throw new AuthenticationException("Pairing verification did not match the encrypted connection.");
        string name = MetadataStore.ValidateName(challenge.Name);
        if (direct && !challenge.Direct)
            throw new ProtocolException("Update Lantoss on both PCs for direct connection.");
        bool accepted = direct || await compare(new(name, channel.Fingerprint, comparison, false), timeout.Token);
        await Wire.WriteAsync(channel.Stream, new()
        {
            Kind = "pair-decision",
            Accepted = accepted
        }, timeout.Token);
        WireMessage final = await Wire.ReadAsync(channel.Stream, timeout.Token, 120);
        Wire.Require(final, "pair-result");
        if (!final.Accepted || !accepted)
            throw new ProtocolException("Pairing was declined. No file access was granted.");
        Peer peer = new()
        {
            Fingerprint = channel.Fingerprint,
            Name = name,
            Address = address.ToString(),
            Port = port,
            SupportsQuickPair = expectedKey != null,
            SupportsDirectConnect = direct,
            KeyVerified = !direct,
            Paired = true
        };
        store.UpsertPeer(peer);
        PeersChanged?.Invoke();
        return peer;
    }
    private async Task AcceptAsync(TcpListener current, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await current.AcceptTcpClientAsync(token);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { break; }
            IPAddress remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            if (Adapter == null || !receiveNetworks.Any(network => network.Contains(remote)) || (!NetworkAdapter.Private(remote) && !(allowLoopback && IPAddress.IsLoopback(remote))) || !connections.Wait(0))
            {
                client.Dispose();
                continue;
            }
            sockets[client] = "";
            RunWorker(() => ServeAsync(client, token));
        }
    }
    private async Task ServeAsync(TcpClient client, CancellationToken serverToken)
    {
        TransferJob? job = null;
        Stream? ssl = null;
        try
        {
            client.NoDelay = true;
            ManagedTls.Connection secure = await ManagedTls.AcceptAsync(client, identity, serverToken);
            ssl = secure.Stream;
            string fingerprint = secure.Fingerprint;
            sockets[client] = fingerprint;
            if (store.Blocked(fingerprint))
                throw new AuthenticationException("This device is blocked.");
            WireMessage hello = await Wire.ReadAsync(ssl, serverToken);
            if (hello.Kind is "pair" or "pair-nearby" or "pair-direct")
            {
                await ReceivePairAsync(ssl, client, fingerprint, hello, serverToken);
                return;
            }
            Peer peer = store.Trusted(fingerprint) ?? throw new AuthenticationException("Pair this device before sending files.");
            if (hello.Kind == "ping")
            {
                await Wire.WriteAsync(ssl, new()
                {
                    Kind = "pong",
                    Name = store.Preferences.DeviceName
                }, serverToken);
                return;
            }
            if (hello.Kind.StartsWith("space-", StringComparison.Ordinal) || hello.Kind.StartsWith("sync-", StringComparison.Ordinal))
            {
                await ServeSpaceAsync(ssl, client, peer, hello, serverToken);
                return;
            }
            Wire.Require(hello, "send");
            Manifest manifest = hello.Manifest ?? throw new ProtocolException("The transfer manifest is missing.");
            FileCatalog.ValidateManifest(manifest);
            job = Transfers.Add(new()
            {
                Id = manifest.Id,
                Direction = "Received",
                PeerName = peer.Name,
                PeerFingerprint = fingerprint,
                PeerAddress = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString(),
                State = TransferState.AwaitingApproval
            });
            job.Initialize(manifest);
            using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(serverToken, job.Cancellation.Token);
            using CancellationTokenRegistration close = work.Token.Register(client.Dispose);
            Preferences preferences = store.Preferences;
            if (ApproveReceive == null)
                throw new ProtocolException("The recipient cannot show an approval dialog.");
            using CancellationTokenSource approvalTimeout = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
            approvalTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            ReceiveDecision decision = await ApproveReceive(new(peer.Name, fingerprint, manifest, preferences.ReceiveFolder, FileCatalog.FreeSpace(preferences.ReceiveFolder), peer.KeyVerified), approvalTimeout.Token);
            if (!decision.Accepted)
            {
                job.State(TransferState.Declined, "Declined by the recipient.");
                await Wire.WriteAsync(ssl, new()
                {
                    Kind = "declined"
                }, work.Token);
                return;
            }
            if (store.Trusted(fingerprint) == null)
                throw new AuthenticationException("The sender is no longer paired.");
            ReceivePlan plan = new(manifest, decision.Folder, decision.Duplicates);
            if (FileCatalog.FreeSpace(plan.Root) < plan.Bytes + 64L * 1024 * 1024)
                throw new IOException("The destination does not have enough free space. Choose another receive folder.");
            if (!receiving.Wait(0))
                throw new ProtocolException("The recipient is already receiving two transfers. Retry shortly.");
            try
            {
                plan.CreateDirectories();
                job.Update(r => r with { Destination = plan.Root, TotalBytes = plan.Bytes, State = TransferState.Receiving });
                foreach (ReceiveItem skipped in plan.Items.Where(i => i.Skip))
                    job.Verified(skipped.Entry, true);
                await Wire.WriteAsync(ssl, new()
                {
                    Kind = "accepted",
                    Skipped = plan.Items.Where(i => i.Skip).Select(i => i.Index).ToArray()
                }, work.Token);
                foreach (ReceiveItem item in plan.Items.Where(i => !i.Skip && !i.Entry.Directory))
                    await ReceiveFileAsync(ssl, client, fingerprint, job, plan, item, work.Token);
                Wire.Require(await Wire.ReadAsync(ssl, work.Token), "finish");
                work.Token.ThrowIfCancellationRequested();
                if (store.Trusted(fingerprint) == null)
                    throw new AuthenticationException("The sender's access was revoked.");
                job.State(TransferState.Verifying);
                await Wire.WriteAsync(ssl, new()
                {
                    Kind = "complete"
                }, work.Token);
                job.State(TransferState.Completed);
            }
            finally { receiving.Release(); }
        }
        catch (Exception error)
        {
            Diagnostic?.Invoke(error);
            if (job != null && !job.Snapshot.Terminal)
                job.State(job.Cancellation.IsCancellationRequested || serverToken.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error));
            if (ssl != null)
            {
                try
                {
                    await Wire.WriteAsync(ssl, new()
                    {
                        Kind = "error",
                        Error = PeerError(error)
                    }, CancellationToken.None);
                }
                catch { }
            }
        }
        finally
        {
            if (ssl != null)
                await ssl.DisposeAsync();
            sockets.TryRemove(client, out _);
            client.Dispose();
            connections.Release();
        }
    }
    private async Task ReceivePairAsync(Stream ssl, TcpClient client, string fingerprint, WireMessage hello, CancellationToken token)
    {
        bool direct = hello.Kind == "pair-direct";
        bool nearbyPair = hello.Kind is "pair-nearby" or "pair-direct";
        if (nearbyPair && !store.Preferences.Discoverable)
            throw new ProtocolException("This PC is hidden from nearby connections. Enable discovery on that PC or use manual connection.");
        string remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        lock (gate)
        {
            if (nearbyPair)
            {
                while (nearbyPairAttempts.TryPeek(out DateTime earlier) && DateTime.UtcNow - earlier >= TimeSpan.FromMinutes(1))
                    nearbyPairAttempts.Dequeue();
                if (nearbyPairAttempts.Count >= 5)
                    throw new ProtocolException("Too many nearby connection requests. Wait one minute and try again.");
                nearbyPairAttempts.Enqueue(DateTime.UtcNow);
            }
            if (attempts.TryGetValue(remote, out var recent) && DateTime.UtcNow - recent.Start < TimeSpan.FromMinutes(1) && recent.Count >= 5)
                throw new ProtocolException("Too many pairing attempts. Wait one minute and try again.");
            if (!attempts.TryGetValue(remote, out recent) || DateTime.UtcNow - recent.Start >= TimeSpan.FromMinutes(1))
                recent = (0, DateTime.UtcNow);
            attempts[remote] = (recent.Count + 1, recent.Start);
            foreach (string old in attempts.Where(p => DateTime.UtcNow - p.Value.Start > TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray())
                attempts.Remove(old);
            if (attempts.Count > 128)
                throw new ProtocolException("Pairing is busy. Try again shortly.");
        }
        if (hello.Nonce.Length != 64 || !hello.Nonce.All(char.IsAsciiHexDigit) || hello.Code.Length > 32)
            throw new ProtocolException("The pairing request is invalid.");
        string name = MetadataStore.ValidateName(hello.Name);
        if (!await pairing.WaitAsync(0, token))
            throw new ProtocolException("Another pairing is in progress. Try again shortly.");
        CancellationTokenSource? pairingTimeout = null;
        try
        {
            if (direct && store.Preferences.RequireConnectionCode && !PairingCode.Consume(hello.Code))
            {
                await Wire.WriteAsync(ssl, new()
                {
                    Kind = "code-required"
                }, token);
                return;
            }
            if (nearbyPair && !direct && store.Preferences.RequireConnectionCode)
                throw new ProtocolException("This PC requires an access code. Update both apps or use Manual connection.");
            if (!direct && !nearbyPair && !PairingCode.Consume(hello.Code))
                throw new ProtocolException("That pairing code is incorrect, expired, or already used. Read the current code on the receiving PC.");
            if (!direct && ApprovePair == null)
                throw new ProtocolException("The receiving PC cannot show a pairing dialog.");
            pairingTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            pairingTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            CancellationTokenSource timeout = pairingTimeout;
            string comparison = Identity.Comparison(Fingerprint, fingerprint, hello.Nonce);
            Task<bool> localDecision = direct ? Task.FromResult(true) : ApprovePair!(new(name, fingerprint, comparison, true), timeout.Token);
            await Wire.WriteAsync(ssl, new()
            {
                Kind = "pair-challenge",
                Name = store.Preferences.DeviceName,
                Fingerprint = Fingerprint,
                Comparison = comparison,
                Direct = direct
            }, timeout.Token);
            WireMessage decision = await Wire.ReadAsync(ssl, timeout.Token, 120);
            Wire.Require(decision, "pair-decision");
            bool accepted = decision.Accepted && await localDecision;
            if (!decision.Accepted)
                timeout.Cancel();
            if (accepted)
            {
                store.UpsertPeer(new()
                {
                    Fingerprint = fingerprint,
                    Name = name,
                    Address = remote,
                    Port = hello.ListenPort is >= 1 and <= 65535 ? hello.ListenPort : 48585,
                    Paired = true,
                    SupportsQuickPair = nearbyPair,
                    SupportsDirectConnect = direct,
                    KeyVerified = !direct
                });
                lock (gate)
                    attempts.Remove(remote);
            }
            await Wire.WriteAsync(ssl, new()
            {
                Kind = "pair-result",
                Accepted = accepted
            }, token);
            PeersChanged?.Invoke();
        }
        finally { pairingTimeout?.Cancel(); pairingTimeout?.Dispose(); pairing.Release(); }
    }
    private async Task ReceiveFileAsync(Stream ssl, TcpClient client, string fingerprint, TransferJob job, ReceivePlan plan, ReceiveItem item, CancellationToken token)
    {
        WireMessage header = await Wire.ReadAsync(ssl, token);
        Wire.Require(header, "file");
        if (header.Index != item.Index)
            throw new ProtocolException("The peer sent files in an unexpected order.");
        FileCatalog.CheckSafePath(item.Destination);
        string parent = Path.GetDirectoryName(item.Destination)!;
        FileCatalog.CheckSafeDirectory(parent);
        Directory.CreateDirectory(parent);
        // Receive into a hidden staging file; verify the hash before publishing it at the final path.
        string temporary = Path.Combine(parent, ".flowshare-" + Guid.NewGuid().ToString("N") + ".part");
        job.StartFile(item.Entry, TransferState.Receiving);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[65536];
                long remaining = item.Entry.Size;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (store.Trusted(fingerprint) == null)
                        throw new AuthenticationException("The sender's access was revoked.");
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    int read = await ssl.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token);
                    if (read == 0)
                        throw new EndOfStreamException("The sender disconnected before the file finished.");
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    remaining -= read;
                    job.Advance(read);
                }
                job.State(TransferState.Verifying);
                await file.FlushAsync(token);
                file.Flush(true);
            }
            job.State(TransferState.Verifying);
            string digest = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(digest, item.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProtocolException("Integrity verification failed. The file was not saved; retry from the original source.");
            token.ThrowIfCancellationRequested();
            if (store.Trusted(fingerprint) == null)
                throw new AuthenticationException("The sender's access was revoked.");
            plan.FinalizeFile(temporary, item);
            job.Verified(item.Entry);
            await Wire.WriteAsync(ssl, new()
            {
                Kind = "verified",
                Index = item.Index,
                Sha256 = digest
            }, token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); GC.KeepAlive(client); }
    }
    public TransferRecord Send(Peer peer, IEnumerable<string> sources)
    {
        if (Transfers.Records.Count(r => r.Direction == "Sent" && !r.Terminal) >= 8)
            throw new InvalidOperationException("Eight outgoing jobs are already queued or active. Wait for one to finish or cancel it.");
        Peer trusted = store.Trusted(peer.Fingerprint) ?? throw new InvalidOperationException("Pair this PC before sending files.");
        List<string> selected = sources.Distinct(StringComparer.OrdinalIgnoreCase).Take(257).ToList();
        if (selected.Count is < 1 or > 256)
            throw new InvalidDataException("Choose between 1 and 256 top-level selections.");
        TransferJob job = Transfers.Add(new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Direction = "Sent",
            PeerName = trusted.Name,
            PeerFingerprint = trusted.Fingerprint,
            PeerAddress = peer.Address,
            PeerPort = peer.Port,
            Sources = selected,
            State = TransferState.Queued
        });
        RunWorker(() => SendJobAsync(job, peer));
        return job.Snapshot;
    }
    public TransferRecord Retry(TransferRecord previous)
    {
        if (previous.Direction != "Sent" || !previous.Terminal)
            throw new InvalidOperationException("Only a finished outgoing job can be retried.");
        Peer peer = Peers.FirstOrDefault(p => p.Fingerprint == previous.PeerFingerprint && p.Paired && !p.Blocked) ?? throw new InvalidOperationException("Pair the recipient again before retrying.");
        return Send(peer, previous.Sources);
    }
    private async Task SendJobAsync(TransferJob job, Peer peer)
    {
        bool acquired = false;
        CancellationToken token = job.Cancellation.Token;
        try
        {
            await sending.WaitAsync(token);
            acquired = true;
            job.State(TransferState.Preparing);
            Manifest manifest = await FileCatalog.BuildAsync(job.Snapshot.Sources, job.Snapshot.Id, true, token, name => job.Update(r => r with { CurrentFile = name }, false));
            job.Initialize(manifest);
            FileCatalog.ValidateManifest(manifest);
            if (store.Trusted(peer.Fingerprint) == null)
                throw new AuthenticationException("The recipient is no longer paired.");
            await using Channel channel = await ConnectPeerAsync(peer, token);
            using CancellationTokenRegistration close = token.Register(channel.Client.Dispose);
            job.State(TransferState.AwaitingApproval);
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "send",
                Name = store.Preferences.DeviceName,
                Manifest = manifest
            }, token);
            WireMessage approval = await Wire.ReadAsync(channel.Stream, token, 125);
            if (approval.Kind == "declined")
            {
                job.State(TransferState.Declined, "The recipient declined this transfer.");
                return;
            }
            Wire.Require(approval, "accepted");
            HashSet<int> skipped = [.. approval.Skipped];
            if (skipped.Any(i => i < 0 || i >= manifest.Entries.Count || manifest.Entries[i].Directory))
                throw new ProtocolException("The recipient sent invalid duplicate-file choices.");
            job.Update(r => r with { TotalBytes = manifest.Entries.Where((_, i) => !skipped.Contains(i)).Sum(e => e.Size) });
            foreach ((ManifestEntry entry, int index) in manifest.Entries.Select((e, i) => (e, i)))
            {
                token.ThrowIfCancellationRequested();
                if (entry.Directory)
                    continue;
                if (skipped.Contains(index))
                {
                    job.Verified(entry, true);
                    continue;
                }
                if (store.Trusted(peer.Fingerprint) == null)
                    throw new AuthenticationException("The recipient's access was revoked.");
                FileCatalog.CheckSafePath(entry.SourcePath);
                FileInfo info = new(entry.SourcePath);
                if (info.Length != entry.Size || info.LastWriteTimeUtc != entry.SourceModifiedUtc)
                    throw new IOException("A source file changed after preparation. Retry to build a new manifest.");
                job.StartFile(entry, TransferState.Sending);
                await Wire.WriteAsync(channel.Stream, new()
                {
                    Kind = "file",
                    Index = index
                }, token);
                await using (FileStream file = new(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[65536];
                    long remaining = entry.Size;
                    while (remaining > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                        if (read == 0)
                            throw new IOException("A source file became shorter during transfer.");
                        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromMinutes(3));
                        await channel.Stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                        remaining -= read;
                        job.Advance(read);
                    }
                }
                job.State(TransferState.Verifying);
                WireMessage result = await Wire.ReadAsync(channel.Stream, token, 300);
                Wire.Require(result, "verified");
                if (result.Index != index || !string.Equals(result.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ProtocolException("The recipient did not verify the original file bytes.");
                job.Verified(entry);
            }
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "finish"
            }, token);
            Wire.Require(await Wire.ReadAsync(channel.Stream, token), "complete");
            token.ThrowIfCancellationRequested();
            job.State(TransferState.Completed);
        }
        catch (Exception error) when (error is IOException or SocketException or AuthenticationException or OperationCanceledException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException or OverflowException)
        {
            if (!job.Snapshot.Terminal)
                job.State(token.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error));
        }
        finally { if (acquired) sending.Release(); }
    }
    private sealed class Channel(TcpClient client, Stream stream, string fingerprint, Action<TcpClient> release) : IAsyncDisposable
    {
        public TcpClient Client { get; } = client; public Stream Stream { get; } = stream; public string Fingerprint { get; } = fingerprint;
        public Action? Closed
        {
            get; set;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Stream.DisposeAsync();
            }
            finally { release(Client); Client.Dispose(); Closed?.Invoke(); }
        }
    }
    private async Task<Channel> ConnectAsync(string address, int port, string? expected, CancellationToken token)
    {
        (IPAddress ip, int validatedPort) = NetworkAdapter.ParseEndpoint($"{address}:{port}", allowLoopback);
        TcpClient client = new(AddressFamily.InterNetwork)
        {
            NoDelay = true
        };
        Stream? ssl = null;
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(ip, validatedPort, timeout.Token);
            ManagedTls.Connection secure = await ManagedTls.ConnectAsync(client, identity, expected, timeout.Token);
            ssl = secure.Stream;
            string fingerprint = secure.Fingerprint;
            sockets[client] = fingerprint;
            return new(client, ssl, fingerprint, c => sockets.TryRemove(c, out _));
        }
        catch { if (ssl != null) await ssl.DisposeAsync(); client.Dispose(); throw; }
    }
    public static string HumanError(Exception error) => error switch
    {
        OperationCanceledException => "The request was cancelled or timed out. Check the other PC and retry.",
        SocketException => "The other PC could not be reached. Check its receiving address, Wi-Fi, firewall Private-network permission, guest isolation, and VPN routing.",
        AuthenticationException => "The peer identity or encrypted connection could not be verified. Pair again and compare the codes on both PCs.",
        UnauthorizedAccessException => "Windows denied access to a file or folder. Choose a local folder you can read or write.",
        EndOfStreamException => "The other PC disconnected before the transfer finished. Retry when it is available.",
        ObjectDisposedException => "The connection closed. Receiving may have stopped or access may have been revoked.",
        _ => error.Message.Length <= 300 ? error.Message : "The transfer could not finish. Check the files and retry."
    };
    private static string PeerError(Exception error) => error switch
    {
        ProtocolException => HumanError(error),
        InvalidDataException => "The incoming filenames, folder paths, or manifest are not valid for Windows.",
        IOException when error.Message.Contains("enough free space", StringComparison.OrdinalIgnoreCase) => "The destination does not have enough free disk space. Choose another receive folder.",
        IOException => "The recipient could not finish the file operation. Check its disk, permissions, and available files.",
        _ => HumanError(error)
    };
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Browser.Dispose();
            Stop();
            lifetime?.Dispose();
        }
    }
}
