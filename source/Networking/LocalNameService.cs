using System.Net;
using System.Net.Sockets;

namespace Lantoss.Networking;

/// <summary>Owns only Lantoss's local names; never changes the router, hosts file or system DNS settings.</summary>
public sealed class LocalNameService : IDisposable
{
    private readonly IPEndPoint group;
    private readonly object gate = new();
    private readonly NetworkAdapter adapter;
    private readonly LocalNameClaim claim;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim pulse = new(0, 1);
    private UdpClient? udp;
    public event Action? Changed;
    public string DeviceUrl
    {
        get
        {
            lock (gate)
                return claim.Ready ? "http://" + claim.Host : "";
        }
    }
    public string Url
    {
        get
        {
            lock (gate)
                return claim.Ready ? "http://" + (claim.OwnsAlias ? LocalNameClaim.SharedHost : claim.Host) : "";
        }
    }
    public bool Accepts(string host)
    {
        lock (gate)
            return claim.Owns(host.TrimEnd('.'));
    }
    public LocalNameService(NetworkAdapter adapter, string key, IPEndPoint? discoveryEndpoint = null)
    {
        this.adapter = adapter;
        claim = new(adapter.Address, key);
        group = discoveryEndpoint ?? new(IPAddress.Parse("224.0.0.251"), 5353);
    }
    public void Start()
    {
        UdpClient socket = new(AddressFamily.InterNetwork)
        {
            ExclusiveAddressUse = false
        };
        try
        {
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, group.Port));
            socket.JoinMulticastGroup(group.Address, adapter.Address);
            socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, adapter.Address.GetAddressBytes());
            socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.MulticastLoopback = true;
            udp = socket;
            _ = ReadLoopAsync();
            _ = ClaimLoopAsync();
        }
        catch { socket.Dispose(); throw; }
    }
    private void Send(byte[] bytes, IPEndPoint? endpoint = null)
    {
        try
        {
            udp?.Send(bytes, bytes.Length, endpoint ?? group);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException) { }
    }
    private void Actions(IEnumerable<NameAction> actions)
    {
        foreach (NameAction action in actions)
            Send(action.Probe ? LocalDns.Probe(action.Host, adapter.Address) : LocalDns.Answer([action.Host], adapter.Address, action.Ttl));
    }
    private async Task ClaimLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TimeSpan delay;
                string before = Url;
                lock (gate)
                {
                    Actions(claim.Tick(DateTime.UtcNow));
                    delay = claim.Delay(DateTime.UtcNow);
                }
                if (before != Url)
                    Changed?.Invoke();
                await pulse.WaitAsync(delay, lifetime.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
    }
    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested && udp != null)
            {
                UdpReceiveResult received = await udp.ReceiveAsync(lifetime.Token);
                if (!adapter.Contains(received.RemoteEndPoint.Address))
                    continue;
                try
                {
                    LocalDnsMessage message = LocalDns.Parse(received.Buffer);
                    if (message.Response && (received.RemoteEndPoint.Address.Equals(adapter.Address) || received.RemoteEndPoint.Port != group.Port))
                        continue;
                    string before = Url;
                    lock (gate)
                    {
                        Actions(claim.Observe(message, DateTime.UtcNow));
                        if (!message.Response)
                        {
                            bool legacy = received.RemoteEndPoint.Port != group.Port;
                            LocalDnsQuestion[] wanted = message.Questions.Where(question => question.Type is 1 or 28 or 255 && claim.Owns(question.Name)).ToArray();
                            string[] names = wanted.Select(question => question.Name).Where(name => legacy || !message.Addresses.Any(answer => answer.Name == name && answer.Address.Equals(adapter.Address) && answer.Ttl >= 15)).Distinct().ToArray();
                            if (names.Length > 0)
                            {
                                Send(LocalDns.Answer(names, adapter.Address, legacy ? 10u : 30u, legacy ? message.Id : (ushort)0, legacy ? wanted : null, wanted.Any(q => q.Type is 28 or 255), legacy),
                                    legacy || wanted.Any(q => q.Unicast) ? received.RemoteEndPoint : null);
                                // Multicast also reaches Windows's DNS client when several responders share UDP 5353.
                                if (!legacy && wanted.Any(q => q.Unicast))
                                    Send(LocalDns.Answer(names, adapter.Address, negativeIpv6: wanted.Any(q => q.Type is 28 or 255)));
                            }
                        }
                    }
                    if (before != Url)
                        Changed?.Invoke();
                    if (pulse.CurrentCount == 0)
                        pulse.Release();
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException) { }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (lifetime.IsCancellationRequested)
                return;
            if (claim.Ready)
                Send(LocalDns.Answer(claim.OwnsAlias ? [claim.Host, LocalNameClaim.SharedHost] : [claim.Host], adapter.Address, 0));
            lifetime.Cancel();
            udp?.Dispose();
            udp = null;
        }
    }
}
