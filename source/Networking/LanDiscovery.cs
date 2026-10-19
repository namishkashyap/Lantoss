using System.Net;
using System.Net.Sockets;
using System.Text;
using Lantoss.Core;

// Local UDP broadcast complements mDNS on home routers. The legacy discovery token is deliberately stable across the Lantoss rename.
namespace Lantoss.Networking;

// A dedicated UDP broadcast port supplements mDNS when Windows services own 5353.
// Like mDNS, these packets are hints only; every connection proves its TLS key.
public sealed class LanDiscovery : IDisposable
{
    public const int DiscoveryPort = 48587;
    private static readonly byte[] Query = Encoding.ASCII.GetBytes("FLOWSHARE-DISCOVER-1");
    private readonly NetworkAdapter[] adapters;
    private readonly string key, name;
    private readonly int receivePort;
    private readonly int discoveryPort;
    private readonly bool visible, locked;
    private readonly CancellationTokenSource lifetime = new();
    private UdpClient? udp;
    private DateTime lastResponse;
    public event Action<Peer>? Found;
    public LanDiscovery(NetworkAdapter[] adapters, string key, string name, int receivePort, bool visible, bool locked, int discoveryPort = DiscoveryPort)
    {
        this.adapters = adapters;
        this.key = key;
        this.name = name;
        this.receivePort = receivePort;
        this.visible = visible;
        this.locked = locked;
        if (discoveryPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(discoveryPort));
        this.discoveryPort = discoveryPort;
    }
    public void Start()
    {
        UdpClient socket = new(AddressFamily.InterNetwork);
        socket.ExclusiveAddressUse = false;
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;
        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            udp = socket;
            _ = ReadAsync();
            _ = PeriodicAsync();
            QueryNow();
        }
        catch { socket.Dispose(); throw; }
    }
    public static IPAddress Broadcast(NetworkAdapter adapter)
    {
        byte[] address = adapter.Address.GetAddressBytes(), mask = adapter.Mask.GetAddressBytes();
        return new IPAddress(Enumerable.Range(0, 4).Select(index => (byte)(address[index] | ~mask[index])).ToArray());
    }
    private void Send(byte[] bytes, IPAddress destination)
    {
        try
        {
            udp?.Send(bytes, bytes.Length, new IPEndPoint(destination, discoveryPort));
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException) { }
    }
    public void QueryNow()
    {
        foreach (NetworkAdapter adapter in adapters)
        {
            IPAddress destination = Broadcast(adapter);
            Send(Query, destination);
            if (visible)
                Send(MdnsDiscovery.BuildAnnouncement(adapter.Address, key, name, receivePort, requireCode: locked), destination);
        }
    }
    private async Task PeriodicAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
                QueryNow();
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task ReadAsync()
    {
        try
        {
            while (udp != null && !lifetime.IsCancellationRequested)
            {
                UdpReceiveResult packet = await udp.ReceiveAsync(lifetime.Token);
                NetworkAdapter? adapter = adapters.FirstOrDefault(item => item.Contains(packet.RemoteEndPoint.Address));
                if (adapter == null || packet.RemoteEndPoint.Port != discoveryPort || packet.Buffer.Length > 4096)
                    continue;
                if (packet.Buffer.AsSpan().SequenceEqual(Query))
                {
                    if (visible && DateTime.UtcNow - lastResponse > TimeSpan.FromSeconds(1))
                    {
                        lastResponse = DateTime.UtcNow;
                        Send(MdnsDiscovery.BuildAnnouncement(adapter.Address, key, name, receivePort, requireCode: locked), packet.RemoteEndPoint.Address);
                    }
                    continue;
                }
                try
                {
                    Peer? found = MdnsDiscovery.ParseAnnouncement(packet.Buffer, packet.RemoteEndPoint.Address);
                    if (found != null && found.Fingerprint != key)
                        Found?.Invoke(found);
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException or FormatException) { }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }
    public void Dispose()
    {
        lifetime.Cancel();
        udp?.Dispose();
    }
}
