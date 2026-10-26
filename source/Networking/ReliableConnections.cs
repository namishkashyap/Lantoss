using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Lantoss.Core;

namespace Lantoss.Networking;

/// <summary>
/// Retries remembered peers without weakening their saved identity pins. Automatic pairing still respects code protection, hidden devices, blocks and rate limits.
/// </summary>
public sealed partial class TransferEngine
{
    private LanDiscovery? lanDiscovery;
    private NetworkAdapter[] receiveNetworks = [];
    private readonly ConcurrentDictionary<string, byte> probes = new(), connecting = new();
    private readonly ConcurrentDictionary<string, DateTime> nextConnect = new();
    public string ConnectionDiagnostics => $"Native receiving: {(Running ? Endpoint : "off")}\nBrowser: {(Browser.Running ? Browser.Url : "off")}\n{DiscoveryStatus}\n" + string.Join("\n", receiveNetworks.Select(network => $"Network: {network.Name} · {network.Address} · mask {network.Mask}")) + $"\nRemembered PCs: {store.Peers.Count(peer => peer.Paired && !peer.Blocked)}\nAutomatic connections: {store.Preferences.AutoConnect}\nPrivate-network inbound access is needed for this app: TCP receive/browser ports and UDP 5353/48587. Router client/guest isolation must allow devices to communicate.";
    private void StartLanDiscovery()
    {
        lanDiscovery?.Dispose();
        lanDiscovery = null;
        if (!Running || Adapter == null || IPAddress.IsLoopback(Adapter.Address))
            return;
        try
        {
            lanDiscovery = new(receiveNetworks, Fingerprint, store.Preferences.DeviceName, Port, store.Preferences.Discoverable, store.Preferences.RequireConnectionCode);
            lanDiscovery.Found += Observe;
            lanDiscovery.Start();
            DiscoveryStatus += " LAN broadcast discovery is also on. Remembered PCs reconnect automatically.";
        }
        catch (SocketException error) { lanDiscovery?.Dispose(); lanDiscovery = null; DiscoveryStatus += " LAN broadcast unavailable: " + error.SocketErrorCode; Diagnostic?.Invoke(error); }
    }
    private async Task MaintainConnectionsAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                RefreshDiscovery();
                foreach (Peer peer in nearby.Values.Where(peer => peer.Online))
                    AutoConnectPeer(peer);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void AutoConnectPeer(Peer peer)
    {
        Preferences preferences = store.Preferences;
        if (!Running || !preferences.AutoConnect || !preferences.Discoverable || preferences.RequireConnectionCode || peer.RequiresCode || !peer.SupportsDirectConnect || store.Trusted(peer.Fingerprint) != null || store.Blocked(peer.Fingerprint) || nextConnect.GetValueOrDefault(peer.Fingerprint) > DateTime.UtcNow || !connecting.TryAdd(peer.Fingerprint, 0))
            return;
        RunWorker(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime?.Token ?? CancellationToken.None);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await ConnectDirectAsync(peer, token: timeout.Token);
                Status?.Invoke("Connected automatically to " + peer.Name + ". Shared folders are available inside the app.");
            }
            catch (Exception error) { nextConnect[peer.Fingerprint] = DateTime.UtcNow.AddMinutes(1); Diagnostic?.Invoke(error); }
            finally { connecting.TryRemove(peer.Fingerprint, out _); }
        });
    }
    private void ProbeInBackground(Peer peer)
    {
        if (!probes.TryAdd(peer.Fingerprint, 0))
            return;
        RunWorker(async () => { try { await ProbeAsync(peer); } finally { probes.TryRemove(peer.Fingerprint, out _); } });
    }
    private async Task<Channel> ConnectPeerAsync(Peer peer, CancellationToken token)
    {
        Peer trusted = store.Trusted(peer.Fingerprint) ?? throw new IOException("Connect this PC in Devices before accessing its files.");
        List<Peer> candidates = [peer];
        if (trusted.Endpoint != peer.Endpoint)
            candidates.Add(trusted);
        if (nearby.TryGetValue(peer.Fingerprint, out Peer? found) && candidates.All(item => item.Endpoint != found.Endpoint))
            candidates.Add(found);
        for (int at = 0; ; at++)
        {
            try
            {
                return await ConnectAsync(candidates[at].Address, candidates[at].Port, peer.Fingerprint, token);
            }
            catch (Exception error) when (at + 1 < candidates.Count && !token.IsCancellationRequested && error is SocketException or OperationCanceledException) { }
        }
    }
}
