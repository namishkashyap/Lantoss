using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Lantoss.Core;

// mDNS discovers advertised endpoints only. An announcement is an untrusted hint; TransferEngine must prove the advertised key during the TLS connection.
namespace Lantoss.Networking;

// DNS-SD over IPv4 multicast DNS. Advertisements contain identity hints and
// reachability metadata only. A TLS pairing/probe must prove the actual key.
public sealed class MdnsDiscovery : IDisposable
{
    public const string Service = "_flowshare._tcp.local";
    private static readonly IPEndPoint Group = new(IPAddress.Parse("224.0.0.251"), 5353);
    private readonly NetworkAdapter adapter;
    private readonly string fingerprint, name;
    private readonly int port;
    private readonly bool visible, requireCode;
    private readonly Func<string>? browserUrl;
    private readonly CancellationTokenSource lifetime = new();
    private UdpClient? udp;
    private DateTime lastReply;
    public event Action<Peer>? Found;
    public MdnsDiscovery(NetworkAdapter adapter, string fingerprint, string name, int port, bool visible, bool requireCode = false, Func<string>? browserUrl = null)
    {
        this.adapter = adapter;
        this.fingerprint = fingerprint;
        this.name = name;
        this.port = port;
        this.visible = visible;
        this.requireCode = requireCode;
        this.browserUrl = browserUrl;
    }
    public void Start()
    {
        UdpClient socket = new(AddressFamily.InterNetwork);
        socket.ExclusiveAddressUse = false;
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            socket.JoinMulticastGroup(Group.Address, adapter.Address);
            socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, adapter.Address.GetAddressBytes());
            socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.MulticastLoopback = true;
            udp = socket;
            _ = ReadLoopAsync();
            _ = PeriodicAsync();
            Query();
            Announce();
        }
        catch { socket.Dispose(); throw; }
    }
    public void Query() => Send(BuildQuery());
    public void Publish() => Announce();
    private void Announce(uint ttl = 75)
    {
        if (!visible)
            return;
        lastReply = DateTime.UtcNow;
        Send(BuildAnnouncement(adapter.Address, fingerprint, name, port, ttl, requireCode: requireCode, browserUrl: browserUrl?.Invoke() ?? ""));
    }
    private void Send(byte[] packet)
    {
        try
        {
            udp?.Send(packet, packet.Length, Group);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException) { }
    }
    private async Task PeriodicAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), lifetime.Token);
                Query();
                Announce();
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
                UdpReceiveResult packet = await udp.ReceiveAsync(lifetime.Token);
                if (packet.Buffer.Length is < 12 or > 4096 || packet.RemoteEndPoint.Port != 5353 || !adapter.Contains(packet.RemoteEndPoint.Address))
                    continue;
                try
                {
                    if ((packet.Buffer[2] & 0x80) == 0)
                    {
                        int offset = 12;
                        string question = ReadName(packet.Buffer, ref offset);
                        if (question == Service && DateTime.UtcNow - lastReply > TimeSpan.FromSeconds(2))
                            Announce();
                    }
                    else
                    {
                        Peer? peer = ParseAnnouncement(packet.Buffer, packet.RemoteEndPoint.Address);
                        if (peer != null && peer.Fingerprint != fingerprint)
                            Found?.Invoke(peer);
                    }
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException or FormatException) { }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }
    public static byte[] BuildQuery()
    {
        using MemoryStream stream = new();
        stream.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        WriteName(stream, Service);
        U16(stream, 12);
        U16(stream, 1);
        return stream.ToArray();
    }
    public static byte[] BuildAnnouncement(IPAddress address, string key, string name, int port, uint ttl = 75, bool quickPair = true, bool directConnect = true, bool requireCode = false, string browserUrl = "")
    {
        string instance = key[..16].ToLowerInvariant() + "." + Service, host = "fs-" + key[..16].ToLowerInvariant() + ".local";
        using MemoryStream packet = new();
        packet.Write(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 4, 0, 0, 0, 0 });
        Record(Service, 12, false, body => WriteName(body, instance));
        Record(instance, 33, true, body => { U16(body, 0); U16(body, 0); U16(body, (ushort)port); WriteName(body, host); });
        Record(instance, 16, true, body =>
        {
            foreach (string text in new[] { "v=1", "id=" + key, "name=" + name, "quick=" + (quickPair ? "1" : "0"), "direct=" + (directConnect ? "1" : "0"), "locked=" + (requireCode ? "1" : "0"), "web=" + browserUrl })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length > 255)
                    throw new InvalidDataException("Device name is too long for discovery.");
                body.WriteByte((byte)bytes.Length);
                body.Write(bytes);
            }
        });
        Record(host, 1, true, body => body.Write(address.GetAddressBytes()));
        return packet.ToArray();
        void Record(string owner, ushort type, bool unique, Action<MemoryStream> write)
        {
            WriteName(packet, owner);
            U16(packet, type);
            U16(packet, unique ? (ushort)0x8001 : (ushort)1);
            U32(packet, ttl);
            using MemoryStream body = new();
            write(body);
            U16(packet, (ushort)body.Length);
            body.Position = 0;
            body.CopyTo(packet);
        }
    }
    public static Peer? ParseAnnouncement(byte[] packet, IPAddress source)
    {
        if (packet.Length is < 12 or > 4096 || (packet[2] & 0x80) == 0)
            return null;
        int questions = Get16(packet, 4), count = Get16(packet, 6) + Get16(packet, 8) + Get16(packet, 10), offset = 12;
        if (questions > 16 || count > 64)
            throw new InvalidDataException("DNS packet has too many records.");
        for (int i = 0; i < questions; i++)
        {
            _ = ReadName(packet, ref offset);
            Need(packet, offset, 4);
            offset += 4;
        }
        Dictionary<string, (string Host, int Port)> servers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IPAddress> addresses = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, string>> metadata = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string owner = ReadName(packet, ref offset);
            Need(packet, offset, 10);
            int type = Get16(packet, offset), length = Get16(packet, offset + 8);
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset + 4, 4));
            offset += 10;
            Need(packet, offset, length);
            int end = offset + length;
            if (ttl > 0 && type == 1 && length == 4)
                addresses[owner] = new IPAddress(packet.AsSpan(offset, 4));
            else if (ttl > 0 && type == 33 && length >= 7)
            {
                int targetOffset = offset + 6;
                string target = ReadName(packet, ref targetOffset);
                if (targetOffset > end)
                    throw new InvalidDataException("Invalid SRV data.");
                servers[owner] = (target, Get16(packet, offset + 4));
            }
            else if (ttl > 0 && type == 16 && owner.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, string> fields = [];
                while (offset < end)
                {
                    int size = packet[offset++];
                    if (offset + size > end)
                        throw new InvalidDataException("Invalid TXT data.");
                    string text = Encoding.UTF8.GetString(packet, offset, size);
                    offset += size;
                    int equals = text.IndexOf('=');
                    if (equals > 0)
                        fields[text[..equals]] = text[(equals + 1)..];
                }
                metadata[owner] = fields;
            }
            offset = end;
        }
        foreach ((string instance, Dictionary<string, string> fields) in metadata)
        {
            if (!fields.TryGetValue("v", out string? version) || version != "1" || !fields.TryGetValue("id", out string? key) || key.Length != 64 || !key.All(char.IsAsciiHexDigit) || !fields.TryGetValue("name", out string? encoded))
                continue;
            if (!servers.TryGetValue(instance, out var service) || service.Port is < 1 or > 65535 || !addresses.TryGetValue(service.Host, out IPAddress? address) || !address.Equals(source))
                continue;
            string name = MetadataStore.ValidateName(encoded);
            string web = fields.GetValueOrDefault("web") ?? "";
            return new Peer { Fingerprint = key.ToUpperInvariant(), Name = name, Address = source.ToString(), Port = service.Port, BrowserUrl = LocalNameClaim.DeviceUrlAllowed(web, key) ? web : "", BrowserAdvertised = fields.ContainsKey("web"), SupportsQuickPair = fields.GetValueOrDefault("quick") == "1", SupportsDirectConnect = fields.GetValueOrDefault("direct") == "1", RequiresCode = fields.GetValueOrDefault("locked") == "1" };
        }
        return null;
    }
    private static string ReadName(byte[] packet, ref int offset, int depth = 0)
    {
        if (depth > 12)
            throw new InvalidDataException("DNS name compression is too deep.");
        List<string> labels = [];
        int bytes = 0;
        while (true)
        {
            Need(packet, offset, 1);
            int length = packet[offset++];
            if (length == 0)
                break;
            if ((length & 0xc0) == 0xc0)
            {
                Need(packet, offset, 1);
                int pointer = ((length & 63) << 8) | packet[offset++];
                labels.Add(ReadName(packet, ref pointer, depth + 1));
                break;
            }
            if (length > 63 || (bytes += length + 1) > 255)
                throw new InvalidDataException("DNS name is too long.");
            Need(packet, offset, length);
            labels.Add(Encoding.UTF8.GetString(packet, offset, length));
            offset += length;
        }
        string name = string.Join('.', labels).ToLowerInvariant();
        if (name.Length > 255)
            throw new InvalidDataException("DNS name is too long.");
        return name;
    }
    private static void Need(byte[] packet, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > packet.Length - count)
            throw new InvalidDataException("Truncated DNS packet.");
    }
    private static int Get16(byte[] packet, int offset)
    {
        Need(packet, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
    }
    private static void WriteName(Stream stream, string name)
    {
        foreach (string label in name.Split('.'))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is < 1 or > 63)
                throw new InvalidDataException("Invalid DNS label.");
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
    }
    private static void U16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
    private static void U32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
    public void Dispose()
    {
        if (lifetime.IsCancellationRequested)
            return;
        Announce(0);
        lifetime.Cancel();
        udp?.Dispose();
        lifetime.Dispose();
    }
}
