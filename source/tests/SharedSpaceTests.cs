using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task SharedSpaceCasesAsync()
    {
        MetadataStore store = Store("Shared space checks");
        TransferRegistry registry = new(store);
        using BrowserPortal portal = new(store, registry, new PairingCode(), true);
        string folder = Path.Combine(root, "Space fixture"), nested = Path.Combine(folder, "Trips");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "An explicitly shared folder.");
        await File.WriteAllTextAsync(Path.Combine(folder, ".flowshare-hidden.part"), "Not a downloadable file.");
        byte[] photo = RandomNumberGenerator.GetBytes(4096);
        await File.WriteAllBytesAsync(Path.Combine(nested, "photo.bin"), photo);
        await File.WriteAllTextAsync(Path.Combine(root, "outside-space.txt"), "Never accessible through the space.");
        SharedSpace space = portal.Spaces.Add(folder, "Family space");
        Check(space.Access == SpaceAccess.ReadOnly && new MetadataStore(store.Root).Spaces.Single().Id == space.Id, "Shared folders default to read only and persist across restarts");
        bool duplicate = false;
        try
        {
            portal.Spaces.Add(folder);
        }
        catch (IOException) { duplicate = true; }
        Check(duplicate, "The same local folder cannot create duplicate space registrations");
        await portal.StartAsync(Loopback, 0);
        using HttpClient client = BrowserClient(portal.Url);
        await ConnectBrowser(client);
        JsonElement spaces = await Body(await client.GetAsync("/api/spaces"));
        Check(spaces.GetArrayLength() == 1 && spaces[0].GetProperty("name").GetString() == "Family space" && !spaces[0].GetProperty("canUpload").GetBoolean() && !spaces.ToString().Contains(folder, StringComparison.Ordinal), "Browser space listings expose names and permissions without local root paths");
        JsonElement entries = await Body(await client.GetAsync("/api/spaces/" + space.Id + "/entries"));
        Check(entries.GetArrayLength() == 2 && entries.EnumerateArray().Any(entry => entry.GetProperty("directory").GetBoolean()) && !entries.ToString().Contains(".flowshare-", StringComparison.Ordinal), "Space browsing lists real folders/files while hiding staging content");
        JsonElement inside = await Body(await client.GetAsync("/api/spaces/" + space.Id + "/entries?path=Trips"));
        Check(inside.GetArrayLength() == 1 && inside[0].GetProperty("path").GetString() == "Trips/photo.bin", "A browser can navigate existing subfolders in an authorized space");
        Check((await client.GetByteArrayAsync("/api/spaces/" + space.Id + "/download?path=Trips%2Fphoto.bin")).SequenceEqual(photo), "Read-only spaces permit exact-byte file downloads");
        Check((await client.GetAsync("/api/spaces/" + space.Id + "/download?path=..%2Foutside-space.txt")).StatusCode == HttpStatusCode.BadRequest, "Shared-space download paths cannot escape the registered root");
        int approvals = 0;
        string outsideDestination = Path.Combine(root, "Wrong destination");
        portal.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(true, outsideDestination)); };
        Check((await client.PostAsJsonAsync("/api/upload-offers", new
        {
            spaceId = space.Id,
            folder = "Trips",
            files = new[] { new { name = "blocked.txt", size = 1 } }
        })).StatusCode == HttpStatusCode.Forbidden && approvals == 0, "Read-only permission rejects uploads in the backend before approval");
        portal.Spaces.Update(space.Id, SpaceAccess.ReadAndUpload, true);
        bool fixedFolder = false;
        portal.ApproveReceive = (request, _) => { approvals++; fixedFolder = request.FixedFolder && request.DefaultFolder == nested; return Task.FromResult(new ReceiveDecision(true, outsideDestination)); };
        JsonElement offer = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            spaceId = space.Id,
            folder = "Trips",
            files = new[] { new { name = "new.txt", size = 3 } }
        }));
        Check((await client.PutAsync("/api/uploads/" + offer.GetProperty("id").GetString() + "/0", new ByteArrayContent([1, 2, 3]))).IsSuccessStatusCode && fixedFolder && File.ReadAllBytes(Path.Combine(nested, "new.txt")).SequenceEqual(new byte[] { 1, 2, 3 }) && !Directory.Exists(outsideDestination), "Writable-space uploads require approval and remain pinned to the chosen space folder");
        JsonElement revoked = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            spaceId = space.Id,
            folder = "",
            files = new[] { new { name = "revoked.txt", size = 1 } }
        }));
        portal.Spaces.Update(space.Id, SpaceAccess.ReadOnly, true);
        Check((await client.PutAsync("/api/uploads/" + revoked.GetProperty("id").GetString() + "/0", new ByteArrayContent([1]))).StatusCode == HttpStatusCode.NotFound && !File.Exists(Path.Combine(folder, "revoked.txt")), "Downgrading space permission revokes already approved upload routes");
        portal.Spaces.Update(space.Id, SpaceAccess.ReadOnly, false);
        Check((await Body(await client.GetAsync("/api/spaces"))).GetArrayLength() == 0 && (await client.GetAsync("/api/spaces/" + space.Id + "/entries")).StatusCode == HttpStatusCode.Forbidden, "Stopping a space removes it from browser listings and denies further browsing");
        portal.Spaces.Remove(space.Id);
        Check(new MetadataStore(store.Root).Spaces.Length == 0 && File.Exists(Path.Combine(nested, "new.txt")), "Removing a space persists revocation and leaves its files untouched");
        string empty = Path.Combine(root, "Missing space fixture");
        Directory.CreateDirectory(empty);
        SharedSpace missing = portal.Spaces.Add(empty);
        Directory.Delete(empty);
        Check(!(await Body(await client.GetAsync("/api/spaces")))[0].GetProperty("available").GetBoolean(), "An unavailable shared drive/folder is reported unavailable rather than recreated");
        portal.Spaces.Remove(missing.Id);
        portal.ApproveReceive = (request, _) => Task.FromResult(new ReceiveDecision(true, request.DefaultFolder));
        JsonElement slow = await Body(await client.PostAsJsonAsync("/api/upload-offers", new
        {
            files = new[] { new { name = "measured.bin", size = 2 * 1024 * 1024 } }
        }));
        Task<HttpResponseMessage> moving = client.PutAsync("/api/uploads/" + slow.GetProperty("id").GetString() + "/0", new PacedUpload());
        bool liveSpeed = false;
        Stopwatch watch = Stopwatch.StartNew();
        while (!moving.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            JsonElement active = await Body(await client.GetAsync("/api/transfers"));
            liveSpeed = active.EnumerateArray().Any(job => job.GetProperty("title").GetString() == "measured.bin" && job.GetProperty("state").GetString() == "Receiving" && job.GetProperty("bytes").GetInt64() > 0 && job.GetProperty("bytesPerSecond").GetDouble() > 0 && job.GetProperty("remainingSeconds").ValueKind == JsonValueKind.Number);
            if (liveSpeed)
                break;
            await Task.Delay(30);
        }
        Check((await moving).IsSuccessStatusCode && liveSpeed, "Live browser transfers report actual streamed bytes, measured speed, and remaining time");
        JsonElement completed = await Body(await client.GetAsync("/api/transfers"));
        Check(completed.EnumerateArray().Any(job => job.GetProperty("title").GetString() == "measured.bin" && job.GetProperty("state").GetString() == "Completed" && job.GetProperty("bytes").GetInt64() == 2 * 1024 * 1024 && job.GetProperty("average").GetBoolean() && job.GetProperty("bytesPerSecond").GetDouble() > 0), "Completed browser transfers retain measured average speed and exact byte totals");
        using HttpClient other = BrowserClient(portal.Url);
        await ConnectBrowser(other);
        Check((await Body(await other.GetAsync("/api/transfers"))).GetArrayLength() == 0, "Browser transfer statistics are isolated to the requesting session");
        portal.Stop();
        await portal.WhenStopped;
        await registry.FlushAsync();
    }
    private sealed class PacedUpload : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 2 * 1024 * 1024;
            return true;
        }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            byte[] chunk = new byte[65536];
            for (int index = 0; index < 32; index++)
            {
                await stream.WriteAsync(chunk);
                await stream.FlushAsync();
                await Task.Delay(50);
            }
        }
    }
}
