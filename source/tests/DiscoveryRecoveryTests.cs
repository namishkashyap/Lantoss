using System.Net;
using System.Net.Sockets;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task DiscoveryRecoveryCasesAsync()
    {
        MetadataStore left = Store("Auto connect first"), right = Store("Auto connect second");
        using Identity a = Identity.Ephemeral(), b = Identity.Ephemeral();
        using TransferEngine first = new(left, a, true), second = new(right, b, true);
        first.Start(Loopback, 0, false);
        second.Start(Loopback, 0, false);
        Peer advertisement = new()
        {
            Fingerprint = b.Fingerprint,
            Name = "Automatic peer",
            Address = "127.0.0.1",
            Port = second.Port,
            SupportsDirectConnect = true
        };
        first.Observe(advertisement);
        await Wait(() => left.Trusted(b.Fingerprint) != null && right.Trusted(a.Fingerprint) != null);
        Check(left.Trusted(b.Fingerprint) is { Paired: true, KeyVerified: false }, "A discovered direct-connect peer is paired automatically through real pinned TLS");
        first.Forget(left.Trusted(b.Fingerprint)!, false);
        second.Forget(right.Trusted(a.Fingerprint)!, false);
        left.SetPreferences(left.Preferences with
        {
            AutoConnect = false
        });
        first.Observe(advertisement);
        await Task.Delay(300);
        Check(left.Trusted(b.Fingerprint) == null, "The automatic connection opt-out prevents discovery from granting saved-key trust");
        left.SetPreferences(left.Preferences with
        {
            AutoConnect = true
        });
        first.Observe(advertisement with
        {
            RequiresCode = true
        });
        await Task.Delay(300);
        Check(left.Trusted(b.Fingerprint) == null, "Discovered code-protected PCs are not connected automatically or bypassed");
        Peer paired = await first.ConnectDirectAsync(advertisement);
        int oldPort = second.Port;
        second.Stop();
        second.Start(Loopback, 0, false);
        first.Observe(advertisement with
        {
            Port = second.Port
        });
        SharedSpace space = second.Browser.Spaces.Add(Directory.CreateDirectory(Path.Combine(right.Root, "Moved endpoint space")).FullName);
        RemoteSpace[] folders = await first.GetRemoteSpacesAsync(paired with
        {
            Port = oldPort
        });
        Check(folders.Single().Id == space.Id && second.Port != oldPort, "A stale saved endpoint falls back to the discovered address while retaining the same TLS key pin");
        first.Forget(left.Trusted(b.Fingerprint)!, true);
        first.Observe(advertisement with
        {
            Port = second.Port
        });
        await Task.Delay(300);
        Check(left.Trusted(b.Fingerprint) == null && left.Blocked(b.Fingerprint), "Automatic discovery never reconnects a blocked public key");
        TaskCompletionSource<Peer> discovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int fixturePort;
        using (UdpClient reservation = new(new IPEndPoint(IPAddress.Loopback, 0)))
            fixturePort = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        using LanDiscovery fallback = new([Loopback], a.Fingerprint, "Broadcast listener", first.Port, true, false, fixturePort);
        fallback.Found += peer => discovered.TrySetResult(peer);
        fallback.Start();
        using UdpClient source = new(AddressFamily.InterNetwork);
        source.ExclusiveAddressUse = false;
        source.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        source.Client.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"), fixturePort));
        byte[] packet = MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("127.0.0.2"), b.Fingerprint, "UDP fallback peer", second.Port);
        await source.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, fixturePort));
        Peer found = await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(found.Fingerprint == b.Fingerprint && found.Port == second.Port && !found.Paired, "The dedicated UDP fallback receives a real announcement as an untrusted discovery hint");
        using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
        await first.ShutdownAsync(shutdown.Token);
        await second.ShutdownAsync(shutdown.Token);
    }
}
