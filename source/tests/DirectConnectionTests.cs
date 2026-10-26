using System.Net;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task DirectConnectionAsync(Identity first, Identity second)
    {
        MetadataStore left = Store("Direct sender"), right = Store("Direct receiver");
        Check(!left.Preferences.RequireConnectionCode && left.Preferences.BrowserSharingEnabled, "Default settings allow direct connections and phone/browser sharing");
        using TransferEngine sender = new(left, first, true), receiver = new(right, second, true);
        int comparisons = 0;
        sender.ApprovePair = receiver.ApprovePair = (_, _) => { comparisons++; return Task.FromResult(false); };
        sender.Start(Loopback, 0, false);
        receiver.Start(Loopback, 0, false);
        Peer nearby = new()
        {
            Fingerprint = second.Fingerprint,
            Name = "Direct receiver",
            Address = "127.0.0.1",
            Port = receiver.Port,
            SupportsDirectConnect = true
        };
        Peer paired = await sender.ConnectDirectAsync(nearby);
        Check(comparisons == 0 && left.Trusted(second.Fingerprint) != null && right.Trusted(first.Fingerprint) != null, "Direct PC connection requires no password, code, or comparison dialog");
        Check(!paired.KeyVerified && paired.TrustLabel == "Connected · saved key", "Direct first connections are labelled saved-key trust rather than verified comparison");
        int approvals = 0;
        receiver.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(true, request.DefaultFolder)); };
        string source = Path.Combine(left.Root, "direct.txt");
        await File.WriteAllTextAsync(source, "Direct does not disable file approval.");
        TransferRecord sent = await Finished(sender, sender.Send(paired, [source]).Id);
        await Finished(receiver, sent.Id, "Received");
        Check(sent.State == TransferState.Completed && approvals == 1 && File.ReadAllText(Path.Combine(right.Preferences.ReceiveFolder, "direct.txt")) == File.ReadAllText(source), "Directly connected PCs still require file approval and preserve exact content");
        receiver.Forget(right.Trusted(first.Fingerprint)!, false);
        sender.Forget(paired, false);
        right.SetPreferences(right.Preferences with
        {
            RequireConnectionCode = true
        });
        receiver.ApplySavedPreferences();
        bool rejected = false;
        try
        {
            await sender.ConnectDirectAsync(nearby);
        }
        catch (CodeRequiredException) { rejected = true; }
        Check(rejected && right.Trusted(first.Fingerprint) == null, "Optional code protection prevents direct binding without a correct code");
        paired = await sender.ConnectDirectAsync(nearby, receiver.PairingCode.Value);
        Check(paired.Paired && right.Trusted(first.Fingerprint) != null && comparisons == 0, "A correct optional access code connects without an additional comparison dialog");
        Check(new MetadataStore(right.Root).Preferences.RequireConnectionCode && sender.Browser.Running, "Security preference persists and automatic browser listener is real");
        left.SetPreferences(left.Preferences with
        {
            BrowserSharingEnabled = false
        });
        sender.ApplySavedPreferences();
        await sender.Browser.WhenStopped;
        Check(!sender.Browser.Running && sender.Running, "Browser sharing can be disabled while native receiving stays on");
        byte[] advertisement = MdnsDiscovery.BuildAnnouncement(IPAddress.Parse("192.168.1.2"), second.Fingerprint, "Direct receiver", 48585, requireCode: true);
        Peer? parsed = MdnsDiscovery.ParseAnnouncement(advertisement, IPAddress.Parse("192.168.1.2"));
        Check(parsed is { SupportsDirectConnect: true, RequiresCode: true, Paired: false }, "Discovery reports direct-connect capability and optional code policy without granting trust");
    }
}
