using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static HttpClient BrowserClient(string url) => new(new HttpClientHandler { CookieContainer = new CookieContainer(), UseProxy = false }) { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<string> ConnectBrowser(HttpClient client)
    {
        JsonElement session = await Body(await client.GetAsync("/api/session"));
        string csrf = session.GetProperty("csrf").GetString()!;
        client.DefaultRequestHeaders.Remove("X-Lantoss-CSRF");
        client.DefaultRequestHeaders.Add("X-Lantoss-CSRF", csrf);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        return csrf;
    }
    private static async Task BrowserCasesAsync()
    {
        MetadataStore store = Store("Browser checks");
        TransferRegistry registry = new(store);
        PairingCode code = new();
        using BrowserPortal portal = new(store, registry, code, true);
        await portal.StartAsync(Loopback, 0);
        string url = portal.Url;
        using HttpClient client = BrowserClient(url);
        Check((await client.GetAsync("/api/files")).StatusCode == HttpStatusCode.Unauthorized, "Browser API requires a same-origin session even in no-code mode");
        string csrf = await ConnectBrowser(client);
        Check(csrf.Length == 64, "Opening the browser page connects directly without entering a code");
        string html = await client.GetStringAsync("/");
        Check(html.Contains("portal.js", StringComparison.Ordinal) && html.Contains("Local sharing · HTTP", StringComparison.Ordinal), "Browser assets are served locally and disclose the actual HTTP transport");
        byte[] original = RandomNumberGenerator.GetBytes(256 * 1024);
        string source = Path.Combine(store.Root, "Selected PC file.bin");
        await File.WriteAllBytesAsync(source, original);
        await File.WriteAllTextAsync(Path.Combine(store.Root, "Private unselected.txt"), "Must not be listed");
        await portal.ShareAsync([source]);
        string listing = await client.GetStringAsync("/api/files");
        JsonElement shared = JsonDocument.Parse(listing).RootElement;
        Check(shared.GetArrayLength() == 1 && !listing.Contains(store.Root, StringComparison.Ordinal) && !listing.Contains("Private unselected", StringComparison.Ordinal), "Browser lists only explicitly selected files and never source paths");
        string id = shared[0].GetProperty("id").GetString()!;
        Check((await client.GetByteArrayAsync("/api/download/" + id)).SequenceEqual(original), "Browser downloads preserve the exact selected PC file bytes");
        using (HttpRequestMessage wrongHost = new(HttpMethod.Get, "/api/files"))
        {
            wrongHost.Headers.Host = "attacker.invalid";
            Check((await client.SendAsync(wrongHost)).StatusCode == HttpStatusCode.Forbidden, "Browser host validation blocks DNS-rebinding hostnames");
        }
        using (HttpRequestMessage crossOrigin = new(HttpMethod.Post, "/api/upload-offers"))
        {
            crossOrigin.Headers.Add("Origin", "http://attacker.invalid");
            crossOrigin.Content = JsonContent.Create(new
            {
                files = new[] { new { name = "bad.txt", size = 1 } }
            });
            Check((await client.SendAsync(crossOrigin)).StatusCode == HttpStatusCode.Forbidden, "Cross-origin upload requests cannot reach file approval");
        }
        client.DefaultRequestHeaders.Remove("X-Lantoss-CSRF");
        Check((await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "bad.txt", size = 1 } }
        })).StatusCode == HttpStatusCode.Forbidden, "Browser upload requests require the session CSRF token");
        client.DefaultRequestHeaders.Add("X-Lantoss-CSRF", csrf);
        Check((await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "../escape.txt", size = 1 } }
        })).StatusCode == HttpStatusCode.BadRequest, "Browser upload names cannot traverse outside the chosen folder");
        Check((await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "large.bin", size = BrowserPortal.MaxUploadBytes + 1 } }
        })).StatusCode == HttpStatusCode.BadRequest, "Browser size ceilings are checked before approval or writes");
        int approvals = 0;
        portal.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(false, request.DefaultFolder)); };
        Check((await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "declined.txt", size = 10 } }
        })).StatusCode == HttpStatusCode.Forbidden && !Directory.Exists(store.Preferences.ReceiveFolder), "Declined phone uploads create no destination files");
        portal.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(true, request.DefaultFolder)); };
        byte[] incoming = RandomNumberGenerator.GetBytes(1024 * 1024);
        int before = approvals;
        JsonElement offer = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "Phone परिवार.bin", size = incoming.Length }, new { name = "empty.txt", size = 0 } }
        }));
        string offerId = offer.GetProperty("id").GetString()!;
        Check(approvals == before + 1 && !File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "Phone परिवार.bin")), "A phone batch is approved once before any file body is saved");
        HttpResponseMessage upload = await client.PutAsync("/api/uploads/" + offerId + "/0", new ByteArrayContent(incoming));
        JsonElement saved = await Body(upload);
        Check(upload.IsSuccessStatusCode && File.ReadAllBytes(Path.Combine(store.Preferences.ReceiveFolder, "Phone परिवार.bin")).SequenceEqual(incoming) && saved.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(incoming)), "Phone uploads stream original bytes into atomic files with a recorded SHA-256 hash");
        using (HttpClient otherBrowser = BrowserClient(url))
        {
            await ConnectBrowser(otherBrowser);
            Check((await otherBrowser.PutAsync("/api/uploads/" + offerId + "/1", new ByteArrayContent([]))).StatusCode == HttpStatusCode.NotFound, "An approved upload route cannot be used by a different browser session");
        }
        Check((await client.PutAsync("/api/uploads/" + offerId + "/0", new ByteArrayContent(incoming))).StatusCode == HttpStatusCode.BadRequest, "Each approved browser file slot is single-use");
        Check((await client.PutAsync("/api/uploads/" + offerId + "/1", new ByteArrayContent([]))).IsSuccessStatusCode && new FileInfo(Path.Combine(store.Preferences.ReceiveFolder, "empty.txt")).Length == 0, "Empty phone files complete a multi-file browser batch");
        JsonElement duplicate = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "empty.txt", size = 1 } }
        }));
        Check((await client.PutAsync("/api/uploads/" + duplicate.GetProperty("id").GetString() + "/0", new ByteArrayContent([42]))).IsSuccessStatusCode && new FileInfo(Path.Combine(store.Preferences.ReceiveFolder, "empty.txt")).Length == 0 && File.ReadAllBytes(Path.Combine(store.Preferences.ReceiveFolder, "empty (1).txt"))[0] == 42, "Browser duplicate handling keeps both copies without overwriting");
        JsonElement cancel = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "cancelled.txt", size = 1 } }
        }));
        string cancelledId = cancel.GetProperty("id").GetString()!;
        await client.PostAsync("/api/uploads/" + cancelledId + "/cancel", new ByteArrayContent([]));
        Check((await client.PutAsync("/api/uploads/" + cancelledId + "/0", new ByteArrayContent([1]))).StatusCode == HttpStatusCode.NotFound && !File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "cancelled.txt")), "Cancelling a browser offer revokes its upload route");
        JsonElement desktopCancel = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "desktop-cancel.txt", size = 1 } }
        }));
        TransferRecord pending = registry.Records.First(record => record.Title == "desktop-cancel.txt");
        registry.Cancel(pending.Id, "Received");
        Check(!(await client.PutAsync("/api/uploads/" + desktopCancel.GetProperty("id").GetString() + "/0", new ByteArrayContent([1]))).IsSuccessStatusCode && !File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "desktop-cancel.txt")) && !Directory.EnumerateFiles(store.Preferences.ReceiveFolder, ".flowshare-*.part").Any(), "Desktop cancellation prevents browser finalization and cleans temporary files");
        JsonElement broken = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "disconnected.bin", size = 1024 * 1024 } }
        }));
        try
        {
            await client.PutAsync("/api/uploads/" + broken.GetProperty("id").GetString() + "/0", new DisconnectingUpload());
        }
        catch (Exception error) when (error is HttpRequestException or IOException) { }
        await Wait(() => registry.Records.Any(record => record.Title == "disconnected.bin" && record.Terminal));
        Check(!File.Exists(Path.Combine(store.Preferences.ReceiveFolder, "disconnected.bin")) && !Directory.EnumerateFiles(store.Preferences.ReceiveFolder, ".flowshare-*.part").Any() && registry.Records.First(record => record.Title == "disconnected.bin").State != TransferState.Completed, "A mid-upload browser disconnect never commits partial content and removes staging files");
        store.SetPreferences(store.Preferences with
        {
            RequireConnectionCode = true
        });
        portal.ApplySecurityPolicy();
        Check((await client.GetAsync("/api/files")).StatusCode == HttpStatusCode.Unauthorized, "Enabling access-code protection revokes existing browser sessions");
        JsonElement locked = await Body(await client.GetAsync("/api/session"));
        Check(!locked.GetProperty("connected").GetBoolean() && locked.GetProperty("requiresCode").GetBoolean(), "Protected browser sessions expose the optional code screen before file access");
        Check((await client.PostAsJsonAsync("/api/login", new
        {
            code = "wrong"
        })).StatusCode == HttpStatusCode.Unauthorized, "Incorrect optional browser codes do not create access");
        string currentCode = code.Value;
        Check((await client.PostAsJsonAsync("/api/login", new
        {
            code = currentCode
        })).IsSuccessStatusCode, "A correct optional browser code creates a browser session");
        await ConnectBrowser(client);
        Check((await client.GetAsync("/api/files")).IsSuccessStatusCode, "A code-authenticated browser can list the selected files");
        using HttpClient replay = BrowserClient(url);
        replay.DefaultRequestHeaders.Add("Origin", url);
        Check((await replay.PostAsJsonAsync("/api/login", new
        {
            code = currentCode
        })).StatusCode == HttpStatusCode.Unauthorized, "Optional browser access codes are single-use");
        await replay.PostAsJsonAsync("/api/login", new
        {
            code = "wrong"
        });
        await replay.PostAsJsonAsync("/api/login", new
        {
            code = "wrong"
        });
        Check((await replay.PostAsJsonAsync("/api/login", new
        {
            code = "wrong"
        })).StatusCode == HttpStatusCode.TooManyRequests, "Optional browser code attempts are rate limited");
        await File.WriteAllTextAsync(source, "Changed after sharing");
        Check((await client.GetAsync("/api/download/" + id)).StatusCode == HttpStatusCode.Conflict, "Changed source files must be shared again before browser download");
        portal.Remove(id);
        Check((await client.GetAsync("/api/download/" + id)).StatusCode == HttpStatusCode.NotFound && File.Exists(source), "Removing a browser share revokes downloads without deleting the original");
        Check(registry.Records.Where(record => record.PeerFingerprint == "browser").All(record => record.Transport == "Browser · HTTP" && record.VerifiedFiles == 0), "Browser history never claims native TLS or source-checksum verification");
        portal.Stop();
        await portal.WhenStopped;
        await registry.FlushAsync();
        Check(!portal.Running && portal.Url.Length == 0, "Stopping sharing closes the actual browser listener");
    }
    private sealed class DisconnectingUpload : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 1024 * 1024;
            return true;
        }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[32768]);
            await stream.FlushAsync();
            await Task.Delay(50);
            throw new IOException("Deliberate browser disconnect test.");
        }
    }
    private static async Task BrowserPreviewServerAsync()
    {
        MetadataStore store = Store("Browser preview fixture");
        TransferRegistry registry = new(store);
        using BrowserPortal portal = new(store, registry, new PairingCode(), true);
        portal.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(true, request.DefaultFolder));
        string source = Path.Combine(store.Root, "Lantoss sample.txt");
        await File.WriteAllTextAsync(source, "A local browser test file. Production invents no shared files.");
        await portal.ShareAsync([source]);
        await portal.StartAsync(Loopback, 0);
        await File.WriteAllTextAsync(Path.Combine(root, "browser-url.txt"), portal.Url);
        Console.WriteLine("TEST BROWSER SERVER " + portal.Url);
        for (int tick = 0; tick < 1200 && !File.Exists(Path.Combine(root, "stop-browser")); tick++)
            await Task.Delay(500);
        portal.Stop();
        await portal.WhenStopped;
        await registry.FlushAsync();
    }
}
