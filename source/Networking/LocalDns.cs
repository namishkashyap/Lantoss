using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Lantoss.Networking;

public sealed record LocalDnsQuestion(string Name, ushort Type, bool Unicast);
public sealed record LocalDnsAddress(string Name, IPAddress Address, uint Ttl);
public sealed record LocalDnsMessage(ushort Id, bool Response, LocalDnsQuestion[] Questions, LocalDnsAddress[] Addresses);

/// <summary>Small bounded DNS codec for local A records. Compression pointers and multi-question queries are supported.</summary>
public static class LocalDns
{
    public static LocalDnsMessage Parse(byte[] packet)
    {
        if (packet.Length is < 12 or > 9000)
            throw new InvalidDataException("Invalid local DNS packet size.");
        if ((packet[2] & 0x78) != 0)
            throw new InvalidDataException("Unsupported local DNS operation.");
        int offset = 12, questions = U16(packet, 4), records = U16(packet, 6) + U16(packet, 8) + U16(packet, 10);
        if (questions > 32 || records > 128)
            throw new InvalidDataException("Too many local DNS records.");
        List<LocalDnsQuestion> query = [];
        for (int i = 0; i < questions; i++)
        {
            string name = Name(packet, ref offset);
            Need(packet, offset, 4);
            ushort type = U16(packet, offset), cls = U16(packet, offset + 2);
            offset += 4;
            if ((cls & 0x7fff) == 1)
                query.Add(new(name, type, (cls & 0x8000) != 0));
        }
        List<LocalDnsAddress> addresses = [];
        for (int i = 0; i < records; i++)
        {
            string name = Name(packet, ref offset);
            Need(packet, offset, 10);
            ushort type = U16(packet, offset), cls = U16(packet, offset + 2), length = U16(packet, offset + 8);
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset + 4, 4));
            offset += 10;
            Need(packet, offset, length);
            if (type == 1 && length == 4 && (cls & 0x7fff) == 1)
                addresses.Add(new(name, new IPAddress(packet.AsSpan(offset, 4)), ttl));
            offset += length;
        }
        return new(U16(packet, 0), (packet[2] & 0x80) != 0, query.ToArray(), addresses.ToArray());
    }
    public static byte[] Probe(string name, IPAddress address)
    {
        using MemoryStream stream = new();
        Header(stream, 0, 0, 1, 0, 1);
        WriteName(stream, name);
        Write16(stream, 255);
        Write16(stream, 1);
        Address(stream, name, address, 30, false);
        return stream.ToArray();
    }
    public static byte[] Answer(IEnumerable<string> names, IPAddress address, uint ttl = 30, ushort id = 0, LocalDnsQuestion[]? echo = null, bool negativeIpv6 = false, bool legacy = false)
    {
        string[] selected = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using MemoryStream stream = new();
        Header(stream, id, 0x8400, (ushort)(echo?.Length ?? 0), (ushort)(selected.Length * (negativeIpv6 ? 2 : 1)), 0);
        foreach (LocalDnsQuestion question in echo ?? [])
        {
            WriteName(stream, question.Name);
            Write16(stream, question.Type);
            Write16(stream, 1);
        }
        foreach (string name in selected)
        {
            Address(stream, name, address, ttl, !legacy);
            if (negativeIpv6)
            {
                WriteName(stream, name);
                Write16(stream, 47);
                Write16(stream, legacy ? (ushort)1 : (ushort)0x8001);
                Write32(stream, ttl);
                using MemoryStream body = new();
                WriteName(body, name);
                body.Write(new byte[] { 0, 6, 0x40, 0, 0, 0, 0, 1 }); // NSEC: A and NSEC exist, AAAA does not.
                Write16(stream, (ushort)body.Length);
                body.Position = 0;
                body.CopyTo(stream);
            }
        }
        return stream.ToArray();
    }
    private static void Address(Stream stream, string name, IPAddress address, uint ttl, bool flush)
    {
        WriteName(stream, name);
        Write16(stream, 1);
        Write16(stream, flush ? (ushort)0x8001 : (ushort)1);
        Write32(stream, ttl);
        Write16(stream, 4);
        stream.Write(address.GetAddressBytes());
    }
    private static void Header(Stream stream, ushort id, ushort flags, ushort questions, ushort answers, ushort authority)
    {
        Write16(stream, id);
        Write16(stream, flags);
        Write16(stream, questions);
        Write16(stream, answers);
        Write16(stream, authority);
        Write16(stream, 0);
    }
    private static void WriteName(Stream stream, string name)
    {
        if (name.Length > 253)
            throw new InvalidDataException("Local hostname is too long.");
        foreach (string label in name.TrimEnd('.').Split('.'))
        {
            byte[] value = Encoding.ASCII.GetBytes(label);
            if (value.Length is < 1 or > 63)
                throw new InvalidDataException("Invalid local hostname label.");
            stream.WriteByte((byte)value.Length);
            stream.Write(value);
        }
        stream.WriteByte(0);
    }
    private static string Name(byte[] packet, ref int offset, int depth = 0)
    {
        if (depth > 16)
            throw new InvalidDataException("DNS compression is too deep.");
        StringBuilder name = new();
        while (true)
        {
            Need(packet, offset, 1);
            int count = packet[offset++];
            if (count == 0)
                break;
            if (name.Length > 0)
                name.Append('.');
            if ((count & 0xc0) == 0xc0)
            {
                Need(packet, offset, 1);
                int pointer = ((count & 63) << 8) | packet[offset++];
                name.Append(Name(packet, ref pointer, depth + 1));
                break;
            }
            if (count > 63 || name.Length + count > 253)
                throw new InvalidDataException("Invalid local DNS name.");
            Need(packet, offset, count);
            name.Append(Encoding.ASCII.GetString(packet, offset, count));
            offset += count;
        }
        if (name.Length > 253)
            throw new InvalidDataException("Local DNS name is too long.");
        return name.ToString().ToLowerInvariant();
    }
    private static void Need(byte[] packet, int offset, int length)
    {
        if (offset < 0 || offset > packet.Length - length)
            throw new InvalidDataException("Truncated local DNS packet.");
    }
    private static ushort U16(byte[] packet, int at) => BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(at, 2));
    private static void Write16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
    private static void Write32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

public sealed record NameAction(string Host, bool Probe, uint Ttl = 30);

/// <summary>Clock-driven claim state, separated from sockets so contention and takeover can be tested deterministically.</summary>
public sealed class LocalNameClaim(IPAddress address, string key, string alias = LocalNameClaim.SharedHost)
{
    public const string SharedHost = "share-wollvo.local";
    /// <summary>The friendly name this PC tries to claim; the admin can choose it in Settings.</summary>
    public string Alias { get; } = alias.ToLowerInvariant();
    private readonly string baseName = DeviceHost(key)[..^6];
    private int suffix = 1, uniqueProbes, aliasProbes;
    private DateTime uniqueDue, aliasDue, announceDue, defended;
    private IPAddress? aliasOwner;
    public string Host { get; private set; } = DeviceHost(key);
    public bool Ready
    {
        get; private set;
    }
    public bool OwnsAlias
    {
        get; private set;
    }
    public static string DeviceHost(string key)
    {
        if (key.Length < 16 || !key.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid local device key.");
        return "share-wollvo-" + key[..16].ToLowerInvariant() + ".local";
    }
    public static bool DeviceUrlAllowed(string? value, string key)
    {
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
            return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        string host = DeviceHost(key);
        if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            return true;
        string prefix = host[..^6] + "-";
        return uri.Host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(uri.Host[prefix.Length..^6], out int suffix) && suffix is >= 2 and <= 32;
    }
    public bool Owns(string host) => Ready && (host.Equals(Host, StringComparison.OrdinalIgnoreCase) || OwnsAlias && host.Equals(Alias, StringComparison.OrdinalIgnoreCase));
    public NameAction[] Tick(DateTime now)
    {
        List<NameAction> actions = [];
        if (!Ready && now >= uniqueDue && suffix <= 32)
        {
            if (uniqueProbes++ < 3)
            {
                actions.Add(new(Host, true));
                uniqueDue = now.AddMilliseconds(250);
            }
            else
            {
                Ready = true;
                actions.Add(new(Host, false));
                announceDue = now.AddSeconds(15);
            }
        }
        if (Ready && !OwnsAlias && now >= aliasDue)
        {
            if (aliasProbes++ < 3)
            {
                actions.Add(new(Alias, true));
                aliasDue = now.AddMilliseconds(250);
            }
            else
            {
                OwnsAlias = true;
                aliasOwner = null;
                defended = default;
                actions.Add(new(Alias, false));
            }
        }
        if (Ready && now >= announceDue)
        {
            actions.Add(new(Host, false));
            if (OwnsAlias)
                actions.Add(new(Alias, false));
            announceDue = now.AddSeconds(15);
        }
        return actions.ToArray();
    }
    public NameAction[] Observe(LocalDnsMessage message, DateTime now)
    {
        List<NameAction> actions = [];
        foreach (LocalDnsAddress other in message.Addresses.Where(item => !item.Address.Equals(address)))
        {
            if (other.Name == Host && other.Ttl > 0 && (message.Response || Compare(other.Address, address) > 0))
            {
                if (Ready)
                    actions.Add(new(Host, false, 0));
                Host = baseName + "-" + ++suffix + ".local";
                Ready = false;
                uniqueProbes = 0;
                uniqueDue = now.AddMilliseconds(250);
                if (OwnsAlias)
                {
                    OwnsAlias = false;
                    actions.Add(new(Alias, false, 0));
                    aliasProbes = 0;
                }
            }
            if (!other.Name.Equals(Alias, StringComparison.OrdinalIgnoreCase))
                continue;
            if (other.Ttl == 0)
            {
                if (aliasOwner?.Equals(other.Address) == true)
                {
                    aliasDue = now.AddMilliseconds(250);
                    aliasProbes = 0;
                    aliasOwner = null;
                }
                continue;
            }
            if (OwnsAlias)
            {
                if (!message.Response || now - defended > TimeSpan.FromSeconds(10))
                {
                    actions.Add(new(Alias, false));
                    if (message.Response)
                        defended = now;
                }
                else
                {
                    OwnsAlias = false;
                    aliasProbes = 0;
                    aliasOwner = other.Address;
                    aliasDue = now.AddSeconds(Math.Clamp(other.Ttl, 2, 120));
                }
            }
            else if (message.Response || aliasProbes > 0 && Compare(other.Address, address) > 0)
            {
                aliasProbes = 0;
                aliasOwner = other.Address;
                aliasDue = now.AddSeconds(message.Response ? Math.Clamp(other.Ttl, 2, 120) : 2);
            }
        }
        return actions.ToArray();
    }
    public TimeSpan Delay(DateTime now)
    {
        if (suffix > 32)
            return TimeSpan.FromSeconds(15);
        DateTime next = !Ready ? uniqueDue : !OwnsAlias && aliasDue < announceDue ? aliasDue : announceDue;
        return TimeSpan.FromMilliseconds(Math.Clamp((next - now).TotalMilliseconds, 50, 15000));
    }
    private static int Compare(IPAddress a, IPAddress b)
    {
        byte[] left = a.GetAddressBytes(), right = b.GetAddressBytes();
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i])
                return left[i].CompareTo(right[i]);
        return 0;
    }
}
