using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Lantoss.Core;

// Length-prefixed protocol messages and streamed file bodies share one channel. Validate message and manifest bounds before allocating or reading file data.
namespace Lantoss.Networking;

public sealed class ProtocolException(string message) : IOException(message);
public sealed class CodeRequiredException(string message) : IOException(message);
public sealed record WireMessage
{
    public required string Kind
    {
        get; init;
    }
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string Nonce { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string Comparison { get; init; } = "";
    public bool Accepted
    {
        get; init;
    }
    public bool Direct
    {
        get; init;
    }
    public string Error { get; init; } = "";
    public Manifest? Manifest
    {
        get; init;
    }
    public int[] Skipped { get; init; } = [];
    public int Index
    {
        get; init;
    }
    public string Sha256 { get; init; } = "";
    public int ListenPort
    {
        get; init;
    }
    public string SpaceId { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string SyncId { get; init; } = "";
    public string ExpectedHash { get; init; } = "";
    public string NoteId { get; init; } = "";
    public string NoteContent { get; init; } = "";
    public string KnownRevision { get; init; } = "";
    public string SnapshotRevision { get; init; } = "";
    public RemoteSpace[] Spaces { get; init; } = [];
    public SpaceEntry[] Entries { get; init; } = [];
}

public static class Wire
{
    public const int MaxFrame = 4 * 1024 * 1024;
    public static async Task WriteAsync(Stream stream, WireMessage message, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, MetadataStore.Json);
        if (body.Length > MaxFrame)
            throw new ProtocolException("The transfer manifest is too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await stream.WriteAsync(header, timeout.Token);
        await stream.WriteAsync(body, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
    public static async Task<WireMessage> ReadAsync(Stream stream, CancellationToken token, int seconds = 30)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxFrame)
            throw new ProtocolException("An invalid protocol frame was rejected.");
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, timeout.Token);
        WireMessage message;
        try
        {
            message = JsonSerializer.Deserialize<WireMessage>(body, MetadataStore.Json) ?? throw new ProtocolException("The peer sent an empty response.");
        }
        catch (JsonException) { throw new ProtocolException("The peer sent an invalid protocol message."); }
        if (message.Version != 1)
            throw new ProtocolException("The peer uses an unsupported protocol version. Update both apps.");
        if (message.Kind is null || message.Kind.Length > 40 || message.Name is null || message.Name.Length > 64 || message.Code is null || message.Code.Length > 32 || message.Nonce is null || message.Nonce.Length > 64 || message.Error is null || message.Error.Length > 300 || message.Fingerprint is null || message.Comparison is null || message.Sha256 is null || message.Skipped is null)
            throw new ProtocolException("The peer sent invalid protocol fields.");
        if (message.Kind == "error")
            throw new ProtocolException(message.Error.Length > 300 ? "The peer rejected the request." : message.Error);
        if (message.NoteId == null || message.NoteId.Length > 32 || message.NoteContent == null || message.NoteContent.Length > SharedNotes.MaxCharacters
            || message.KnownRevision == null || message.KnownRevision.Length > 64 || message.SnapshotRevision == null || message.SnapshotRevision.Length > 64)
            throw new ProtocolException("Invalid note or snapshot fields.");
        if (message.SpaceId is null || message.SpaceId.Length > 32 || message.SyncId is null || message.SyncId.Length > 32 || message.RelativePath is null || message.RelativePath.Length > 2048 || message.ExpectedHash is null || message.ExpectedHash.Length > 64 || message.Spaces is null || message.Spaces.Length > 32 || message.Entries is null || message.Entries.Length > 1000)
            throw new ProtocolException("Invalid shared-folder protocol fields.");
        return message;
    }
    public static void Require(WireMessage message, string kind)
    {
        if (message.Kind != kind)
            throw new ProtocolException("Unexpected peer response. Update both apps and try again.");
    }
}

public sealed record NetworkAdapter(string Name, IPAddress Address, IPAddress Mask, bool Wireless)
{
    public override string ToString() => $"{Name} · {Address}";
    public bool Contains(IPAddress address)
    {
        byte[] a = Address.GetAddressBytes(), b = address.GetAddressBytes(), mask = Mask.GetAddressBytes();
        return a.Length == 4 && b.Length == 4 && Enumerable.Range(0, 4).All(i => (a[i] & mask[i]) == (b[i] & mask[i]));
    }
    public static bool Private(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }
    public static NetworkAdapter[] Available() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
        .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => Private(a.Address)).Select(a => new NetworkAdapter(n.Name, a.Address, a.IPv4Mask, n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)))
        .OrderByDescending(a => a.Wireless).ToArray();
    public static (IPAddress Address, int Port) ParseEndpoint(string input, bool allowLoopback = false)
    {
        string[] parts = input.Trim().Split(':');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address) || !int.TryParse(parts[1], out int port) || port is < 1 or > 65535 || (!Private(address) && !(allowLoopback && IPAddress.IsLoopback(address))))
            throw new InvalidDataException("Enter the private IPv4 address and port shown on the other PC, for example 192.168.1.20:48585.");
        return (address, port);
    }
}
