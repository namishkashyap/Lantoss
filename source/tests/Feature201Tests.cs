using System.Net;
using System.Net.Sockets;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task Version201CasesAsync()
    {
        static bool Rejects(Func<string> action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException) { return true; }
        }
        Check(Profiles.AddressName(" Family-Share ") == "family-share" && Profiles.AddressName("lantoss.local") == "lantoss" && Rejects(() => Profiles.AddressName("-bad")) && Rejects(() => Profiles.AddressName("has space"))
            && Rejects(() => Profiles.AddressName("")) && Rejects(() => Profiles.AddressName(new string('a', 41))) && Rejects(() => Profiles.AddressName("dot.inside")), "The admin's phone-page address is one lowercase DNS label");
        MetadataStore store = Store("Custom address");
        Check(store.Preferences.LocalName == "share-wollvo", "The default phone-page address stays share-wollvo");
        store.SetPreferences(store.Preferences with
        {
            LocalName = "Family-Share.local"
        });
        bool invalid = false;
        try
        {
            store.SetPreferences(store.Preferences with
            {
                LocalName = "no spaces"
            });
        }
        catch (InvalidDataException) { invalid = true; }
        Check(new MetadataStore(store.Root).Preferences.LocalName == "family-share" && invalid, "A custom address is normalized, saved, and invalid names are refused");
        string key = new('D', 64);
        LocalNameClaim claim = new(IPAddress.Parse("192.168.1.10"), key, "family-share.local");
        DateTime now = DateTime.UtcNow;
        for (int tick = 0; tick < 12; tick++)
            claim.Tick(now.AddMilliseconds(tick * 300));
        Check(claim.OwnsAlias && claim.Owns("FAMILY-SHARE.local") && !claim.Owns(LocalNameClaim.SharedHost), "A PC claims the admin's custom address instead of the default name");
        using UdpClient reserve = new(new IPEndPoint(IPAddress.Loopback, 0));
        int isolatedPort = ((IPEndPoint)reserve.Client.LocalEndPoint!).Port;
        reserve.Close();
        using LocalNameService service = new(Loopback, key, new IPEndPoint(IPAddress.Parse("239.255.45.20"), isolatedPort), "family-share.local", 48586);
        service.Start();
        await Wait(() => service.Url == "http://family-share.local:48586", 8);
        Check(service.DeviceUrl.EndsWith(".local:48586", StringComparison.Ordinal) && LocalNameClaim.DeviceUrlAllowed(service.DeviceUrl, key), "Names are published when port 80 is unavailable, with the browser port in the link");
    }
}
