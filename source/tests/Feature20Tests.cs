using System.Net;
using System.Net.Http.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task Version20CasesAsync()
    {
        static bool Rejects(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException) { return true; }
        }
        Check(Profiles.CleanName("  Priya's phone ") == "Priya's phone" && Rejects(() => Profiles.CleanName("Ana\u202Eenohp")) && Rejects(() => Profiles.CleanName(new string('a', 41))) && Rejects(() => Profiles.CleanName("a\nb")), "Visitor names are trimmed, bounded and reject control and bidi formatting characters");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1];
        Check(Profiles.FromDataUrl("data:image/png;base64," + Convert.ToBase64String(png))!.SequenceEqual(png) && Rejects(() => Profiles.FromDataUrl("data:image/svg+xml;base64,PHN2Zz4=")) && Rejects(() => Profiles.FromDataUrl("data:image/png;base64," + Convert.ToBase64String(new byte[12]))) && Rejects(() => Profiles.FromDataUrl("data:image/jpeg;base64,!!")), "Profile pictures accept only small PNG or JPEG data");
        string inbox = Path.Combine(Path.GetTempPath(), "Lantoss Inbox");
        Check(Profiles.SenderFolder(inbox, "Ana (iPhone · Safari)") == Path.Combine(inbox, "Ana (iPhone · Safari)") && Profiles.SenderFolder(inbox, "..") == Path.Combine(inbox, "Unknown sender") && Profiles.SenderFolder(inbox, "CON") == Path.Combine(inbox, "Unknown sender") && Profiles.SenderFolder(inbox, "a/b\\c:d") == Path.Combine(inbox, "a_b_c_d"), "Sender folders stay one safe folder below the inbox");
        Check(Profiles.DeviceLabel("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1") == "iPhone · Safari" && Profiles.DeviceLabel("Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36") == "Android phone · Chrome", "Browser devices are labelled by device type and browser");
        PrivateFirewallPlan curly = new(Path.Combine(Path.GetTempPath(), "a\u2019;Start-Process calc;\u2018", "Lantoss.exe"), [48585]);
        Check(curly.SetupScript.Contains("a\u2019\u2019;Start-Process calc;\u2018\u2018", StringComparison.Ordinal), "Curly single quotes in the app path are escaped in the elevated firewall script");
        MetadataStore store = Store("Version 2 portal");
        using BrowserPortal portal = new(store, new(store), new(), true);
        await portal.StartAsync(Loopback, 0);
        using HttpClient browser = BrowserClient(portal.Url);
        await ConnectBrowser(browser);
        HttpResponseMessage named = await browser.PostAsJsonAsync("/api/profile", new
        {
            name = "Priya",
            picture = "data:image/png;base64," + Convert.ToBase64String(png)
        });
        BrowserDevice device = portal.Devices.Single();
        Check(named.IsSuccessStatusCode && device.Name == "Priya" && device.Picture!.SequenceEqual(png) && device.Address == "127.0.0.1", "A connected browser appears in the PC's device list with its chosen name and picture");
        Check((await browser.PostAsJsonAsync("/api/profile", new
        {
            name = "Bad\u202Ename"
        })).StatusCode == HttpStatusCode.BadRequest && portal.Devices.Single().Name == "Priya", "Invalid browser names are rejected without changing the device list");
        portal.Disconnect(device.Handle);
        Check(portal.Devices.Length == 0 && (await browser.GetAsync("/api/notes")).StatusCode == HttpStatusCode.Unauthorized, "Disconnecting a browser ends its session");
        for (int i = 0; i < 6; i++)
        {
            using HttpClient extra = BrowserClient(portal.Url);
            await ConnectBrowser(extra);
        }
        Check(portal.Devices.Length == 4, "One address cannot fill the browser session table; it replaces its own oldest sessions");
    }
}
