using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lantoss.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Local HTTP portal for phones and browsers. Same-origin, Host, session and CSRF checks restrict requests; HTTP itself does not encrypt the connection.
namespace Lantoss.Networking;

public sealed record BrowserFile(string Id, string Name, long Size, string Path, DateTime ModifiedUtc);
public sealed record BrowserUploadFile(string Name, long Size);
public sealed record BrowserUploadRequest(List<BrowserUploadFile> Files, string SpaceId = "", string Folder = "");
public sealed record BrowserLogin(string Code);
public sealed record BrowserNote(string Content);

// LAN-only HTTP portal. This is deliberately separate from the native TLS protocol.
public sealed partial class BrowserPortal : IDisposable
{
    public const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;
    private readonly MetadataStore store;
    private readonly TransferRegistry transfers;
    private readonly PairingCode code;
    private readonly bool allowLoopback;
    private readonly object gate = new();
    private readonly Dictionary<string, BrowserFile> files = [];
    private readonly Dictionary<string, Session> sessions = [];
    private readonly HashSet<string> busySessions = [];
    private readonly Dictionary<string, (DateTime Start, int Count)> attempts = [];
    private readonly Dictionary<string, DateTime> noteAttempts = [];
    private readonly ConcurrentDictionary<string, Offer> offers = new();
    private readonly SemaphoreSlim streams = new(3, 3);
    private WebApplication? server;
    private NetworkAdapter? adapter;
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource accessLifetime = new();
    private bool protectedMode;
    private readonly string deviceKey;
    private LocalNameService? localNames;
    private int generation;
    private sealed record Session(string Id, string Csrf, string Address, DateTime CreatedUtc, int Generation);
    private sealed class Offer(string id, string session, string folder, Manifest manifest, TransferJob job, string spaceId = "", string spaceRoot = "")
    {
        public string Id { get; } = id;
        public string Session { get; } = session;
        public string Folder { get; } = folder;
        public Manifest Manifest { get; } = manifest;
        public TransferJob Job { get; } = job;
        public string SpaceId { get; } = spaceId;
        public string SpaceRoot { get; } = spaceRoot;
        public DateTime ExpiresUtc { get; } = DateTime.UtcNow.AddMinutes(30);
        public int[] States { get; } = new int[manifest.Entries.Count];
        public int InFlight;
        public CancellationTokenSource Cancellation { get; } = new();
    }
    public event Action? Changed;
    public Func<ReceiveRequest, CancellationToken, Task<ReceiveDecision>>? ApproveReceive
    {
        get; set;
    }
    public string IpUrl { get; private set; } = "";
    public string Url => localNames?.Url is { Length: > 0 } name ? name : IpUrl;
    public string DeviceUrl => localNames?.DeviceUrl ?? "";
    public string LocalAddressStatus { get; private set; } = "Start browser sharing to enable the local name.";
    public Func<object[]>? AvailableBrowserDevices
    {
        get; set;
    }
    public SharedNotes Notes
    {
        get;
    }
    public bool Running => server != null;
    public Task WhenStopped { get; private set; } = Task.CompletedTask;
    public BrowserFile[] Files
    {
        get
        {
            lock (gate)
                return files.Values.ToArray();
        }
    }
    public BrowserPortal(MetadataStore store, TransferRegistry transfers, PairingCode code, bool allowLoopback = false, string? deviceKey = null)
    {
        this.store = store;
        Notes = new(store.Root);
        this.transfers = transfers;
        this.code = code;
        this.allowLoopback = allowLoopback;
        this.deviceKey = deviceKey ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store.Root)));
        protectedMode = store.Preferences.RequireConnectionCode;
        Spaces = new(store);
        Spaces.Changed += RevokeSpace;
    }
    public async Task StartAsync(NetworkAdapter network, int port = 80)
    {
        if (Running)
            return;
        if (!NetworkAdapter.Private(network.Address) && !(allowLoopback && IPAddress.IsLoopback(network.Address)))
            throw new InvalidOperationException("Choose a private local network for browser sharing.");
        await WhenStopped.ConfigureAwait(false);
        adapter = network;
        lifetime = new();
        WebApplication? candidate = null;
        int[] choices = port == 80 ? [80, 48586, 0] : port == 0 ? [0] : [port, 0];
        for (int index = 0; index < choices.Length; index++)
        {
            candidate = CreateServer(network, choices[index]);
            try
            {
                await candidate.StartAsync(lifetime.Token).ConfigureAwait(false);
                break;
            }
            catch (Exception error) when ((error is IOException or System.Net.Sockets.SocketException) && index < choices.Length - 1) { await candidate.DisposeAsync().ConfigureAwait(false); candidate = null; }
            catch { await candidate.DisposeAsync().ConfigureAwait(false); throw; }
        }
        if (candidate == null)
            throw new IOException("No browser listening port is available.");
        server = candidate;
        IpUrl = candidate.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
        RefreshLocalAddress();
        Changed?.Invoke();
    }
    public void RefreshLocalAddress()
    {
        if (!Running || adapter == null)
            return;
        bool wanted = !allowLoopback && store.Preferences.Discoverable && new Uri(IpUrl).Port == 80;
        if (wanted && localNames != null)
            return;
        string before = Url;
        localNames?.Dispose();
        localNames = null;
        if (wanted)
        {
            LocalNameService names = new(adapter, deviceKey);
            names.Changed += () =>
            {
                if (!ReferenceEquals(localNames, names))
                    return;
                LocalAddressStatus = names.DeviceUrl.Length > 0
                    ? "Local address ready. The shared name may open another PC; use its PC picker or this PC's link below."
                    : "Checking the local name…";
                Changed?.Invoke();
            };
            try
            {
                localNames = names;
                names.Start();
                LocalAddressStatus = "Checking the local name…";
            }
            catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException)
            {
                names.Dispose();
                localNames = null;
                LocalAddressStatus = "Local-name discovery is unavailable. Scan the direct-link QR code instead.";
            }
        }
        else
            LocalAddressStatus = allowLoopback ? "Preview uses loopback only; local names are not registered."
            : !store.Preferences.Discoverable ? "PC discovery is off. Enable it in Settings for automatic local names; the direct QR link still works."
            : "Port 80 is already in use or unavailable. Close the other local web server to enable the no-port address; the direct QR link still works.";
        if (before != Url)
            Changed?.Invoke();
    }
    private WebApplication CreateServer(NetworkAdapter network, int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(BrowserPortal).Assembly.FullName, ContentRootPath = store.Root, EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(network.Address, port);
            options.Limits.MaxConcurrentConnections = 24;
            options.Limits.MaxRequestBodySize = MaxUploadBytes;
            options.Limits.MaxRequestHeadersTotalSize = 16384;
            options.Limits.MaxRequestLineSize = 4096;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        WebApplication app = builder.Build();
        app.Run(HandleAsync);
        return app;
    }
    public void ApplySecurityPolicy()
    {
        RefreshLocalAddress();
        if (protectedMode == store.Preferences.RequireConnectionCode)
            return;
        protectedMode = store.Preferences.RequireConnectionCode;
        ResetConnections();
    }
    public void ResetConnections()
    {
        lock (gate)
        {
            generation++;
            sessions.Clear();
            noteAttempts.Clear();
            browserJobs.Clear();
            busySessions.Clear();
            accessLifetime.Cancel();
            accessLifetime = new();
        }
        foreach (Offer offer in offers.Values)
        {
            offer.Cancellation.Cancel();
            offer.Job.Cancel();
        }
        offers.Clear();
        Changed?.Invoke();
    }
    public void Stop()
    {
        WebApplication? previous = server;
        server = null;
        localNames?.Dispose();
        localNames = null;
        IpUrl = "";
        LocalAddressStatus = "Browser sharing is off.";
        lifetime.Cancel();
        ResetConnections();
        if (previous != null)
            WhenStopped = StopAsync(previous);
        Changed?.Invoke();
    }
    private static async Task StopAsync(WebApplication app)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        try
        {
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        await app.DisposeAsync().ConfigureAwait(false);
    }
    public async Task ShareAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        Manifest selected = await FileCatalog.BuildAsync(paths, Guid.NewGuid().ToString("N"), false, token);
        lock (gate)
        {
            if (files.Count + selected.FileCount > 1000)
                throw new IOException("Share up to 1,000 browser files at a time.");
            foreach (ManifestEntry entry in selected.Entries.Where(e => !e.Directory))
            {
                if (files.Values.Any(f => f.Path.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string id = Guid.NewGuid().ToString("N");
                files[id] = new(id, entry.RelativePath, entry.Size, entry.SourcePath, entry.SourceModifiedUtc);
            }
        }
        Changed?.Invoke();
    }
    public void Remove(string id)
    {
        lock (gate)
            files.Remove(id);
        Changed?.Invoke();
    }
    public void ClearFiles()
    {
        lock (gate)
            files.Clear();
        Changed?.Invoke();
    }
    private static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private Session? GetSession(HttpContext context)
    {
        string? key = context.Request.Cookies["LantossSession"];
        lock (gate)
        {
            if (key != null && sessions.TryGetValue(key, out Session? found) && found.Generation == generation && found.Address == context.Connection.RemoteIpAddress?.ToString() && DateTime.UtcNow - found.CreatedUtc < TimeSpan.FromHours(12))
                return found;
        }
        return null;
    }
    private Session CreateSession(HttpContext context)
    {
        lock (gate)
        {
            foreach (string old in sessions.Where(s => DateTime.UtcNow - s.Value.CreatedUtc >= TimeSpan.FromHours(12)).Select(s => s.Key).ToArray())
                sessions.Remove(old);
            if (sessions.Count >= 64)
                throw new IOException("Too many browsers are connected. Reset browser connections in Lantoss.");
            Session session = new(RandomToken(), RandomToken(), context.Connection.RemoteIpAddress!.ToString(), DateTime.UtcNow, generation);
            sessions[session.Id] = session;
            context.Response.Cookies.Append("LantossSession", session.Id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(12), IsEssential = true });
            return session;
        }
    }
    private bool SameOrigin(HttpContext context)
    {
        string origin = context.Request.Headers.Origin.ToString();
        return LocalBrowserAccess.SameOrigin(origin, context.Request.Host.Host, context.Request.Host.Port, context.Request.Headers["Sec-Fetch-Site"].ToString());
    }
    private bool AllowedHost(HostString host) => LocalBrowserAccess.HostAllowed(IpUrl, host.Host, host.Port, name => localNames?.Accepts(name) == true);
    private static bool Csrf(HttpContext context, Session session) => context.Request.Headers["X-Lantoss-CSRF"].ToString() == session.Csrf;
    private async Task HandleAsync(HttpContext context)
    {
        try
        {
            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (adapter == null || remote == null || !adapter.Contains(remote) || (!NetworkAdapter.Private(remote) && !(allowLoopback && IPAddress.IsLoopback(remote))) || !AllowedHost(context.Request.Host))
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'self'";
            string path = context.Request.Path.Value ?? "/";
            bool get = HttpMethods.IsGet(context.Request.Method);
            if (get && path is "/" or "/portal.js" or "/portal.css")
            {
                string asset = path == "/" ? "portal.html" : path[1..];
                await using Stream source = typeof(BrowserPortal).Assembly.GetManifestResourceStream("Lantoss.Browser." + asset) ?? throw new IOException("Browser interface is missing.");
                context.Response.ContentType = asset.EndsWith(".html") ? "text/html; charset=utf-8" : asset.EndsWith(".css") ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";
                await source.CopyToAsync(context.Response.Body, context.RequestAborted);
                return;
            }
            if (!get && !SameOrigin(context))
            {
                context.Response.StatusCode = 403;
                return;
            }
            Session? session = GetSession(context);
            if (get && path == "/api/session")
            {
                if (session == null && !store.Preferences.RequireConnectionCode)
                    session = CreateSession(context);
                await Json(context, new
                {
                    connected = session != null,
                    requiresCode = store.Preferences.RequireConnectionCode,
                    csrf = session?.Csrf,
                    name = store.Preferences.DeviceName,
                    maxFileBytes = MaxUploadBytes
                });
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method) && path == "/api/login")
            {
                string remoteAddress = remote.ToString();
                lock (gate)
                {
                    foreach (string old in attempts.Where(p => DateTime.UtcNow - p.Value.Start >= TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray())
                        attempts.Remove(old);
                    if (attempts.Count > 128 || (attempts.TryGetValue(remoteAddress, out var previous) && previous.Count >= 5))
                    {
                        context.Response.StatusCode = 429;
                        return;
                    }
                    var current = attempts.GetValueOrDefault(remoteAddress, (DateTime.UtcNow, 0));
                    attempts[remoteAddress] = (current.Item1, current.Item2 + 1);
                }
                BrowserLogin login = await ReadJson<BrowserLogin>(context);
                if (store.Preferences.RequireConnectionCode && (login.Code == null || login.Code.Length > 32 || !code.Consume(login.Code)))
                {
                    context.Response.StatusCode = 401;
                    await Json(context, new
                    {
                        error = "The code is incorrect, expired, or already used."
                    });
                    return;
                }
                session = CreateSession(context);
                await Json(context, new
                {
                    connected = true,
                    csrf = session.Csrf
                });
                return;
            }
            if (session == null)
            {
                context.Response.StatusCode = 401;
                await Json(context, new
                {
                    error = "Open the sharing page to connect."
                });
                return;
            }
            if (!get && !Csrf(context, session))
            {
                context.Response.StatusCode = 403;
                return;
            }
            if (get && path == "/api/files")
            {
                await Json(context, Files.Select(f => new { id = f.Id, name = f.Name, size = f.Size }));
                return;
            }
            if (get && path == "/api/devices")
            {
                await Json(context, AvailableBrowserDevices?.Invoke() ?? []);
                return;
            }
            if (get && path == "/api/notes")
            {
                await Json(context, Notes.Snapshot(context.Request.Query["revision"].ToString()));
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method) && path == "/api/notes")
            {
                lock (gate)
                {
                    if (noteAttempts.TryGetValue(session.Id, out DateTime last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(1))
                    {
                        context.Response.StatusCode = 429;
                        return;
                    }
                    if (noteAttempts.Count >= 64)
                        noteAttempts.Clear();
                    noteAttempts[session.Id] = DateTime.UtcNow;
                }
                BrowserNote note = await ReadJson<BrowserNote>(context, 100000);
                SharedNote saved = Notes.Add(note.Content, "Phone / browser");
                await Json(context, saved);
                return;
            }
            if (get && path == "/api/transfers")
            {
                await Json(context, TransferViews(session.Id));
                return;
            }
            if (get && path == "/api/spaces")
            {
                await Json(context, Spaces.Spaces.Where(space => space.Enabled).Select(space => new { id = space.Id, name = space.Name, canUpload = space.Access == SpaceAccess.ReadAndUpload, available = SpaceCatalog.Available(space) }));
                return;
            }
            string[] spaceRoute = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (get && spaceRoute.Length == 4 && spaceRoute[0] == "api" && spaceRoute[1] == "spaces")
            {
                string relative = context.Request.Query["path"].ToString();
                if (spaceRoute[3] == "entries")
                {
                    await Json(context, Spaces.List(spaceRoute[2], relative));
                    return;
                }
                if (spaceRoute[3] == "download")
                {
                    await DownloadSpaceAsync(context, spaceRoute[2], relative, session);
                    return;
                }
            }
            if (get && path.StartsWith("/api/download/", StringComparison.Ordinal))
            {
                await DownloadAsync(context, path[14..], session);
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method) && path == "/api/upload-offers")
            {
                await OfferAsync(context, session);
                return;
            }
            string[] route = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (route.Length == 4 && route[0] == "api" && route[1] == "uploads" && offers.TryGetValue(route[2], out Offer? offer) && offer.Session == session.Id)
            {
                if (HttpMethods.IsPost(context.Request.Method) && route[3] == "cancel")
                {
                    offer.Cancellation.Cancel();
                    offer.Job.Cancel();
                    RemoveOffer(offer);
                    await Json(context, new
                    {
                        cancelled = true
                    });
                    return;
                }
                if (HttpMethods.IsPut(context.Request.Method) && int.TryParse(route[3], out int index) && index >= 0 && index < offer.States.Length)
                {
                    await UploadAsync(context, offer, index);
                    return;
                }
            }
            context.Response.StatusCode = 404;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or OperationCanceledException or BadHttpRequestException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = error is SpacePermissionException ? 403 : error is OperationCanceledException ? 409 : error is InvalidDataException or JsonException or BadHttpRequestException ? 400 : 409;
                try
                {
                    await Json(context, new
                    {
                        error = error is OperationCanceledException ? "The transfer stopped or was declined. Try again when the PC is ready." : TransferEngine.HumanError(error)
                    });
                }
                catch (Exception nested) when (nested is IOException or OperationCanceledException) { }
            }
            else
                context.Abort();
        }
    }
    private static async Task<T> ReadJson<T>(HttpContext context, int maximum = 32768)
    {
        if (context.Request.ContentLength > maximum)
            throw new InvalidDataException("This request is too large.");
        IHttpMaxRequestBodySizeFeature? limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
            limit.MaxRequestBodySize = maximum;
        return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 }, context.RequestAborted) ?? throw new InvalidDataException("The request is empty.");
    }
    private static Task Json(HttpContext context, object value) => context.Response.WriteAsJsonAsync(value, context.RequestAborted);
    private async Task OfferAsync(HttpContext context, Session session)
    {
        foreach (Offer expired in offers.Values.Where(o => o.ExpiresUtc < DateTime.UtcNow))
        {
            expired.Cancellation.Cancel();
            expired.Job.Cancel();
            RemoveOffer(expired);
        }
        BrowserUploadRequest request = await ReadJson<BrowserUploadRequest>(context);
        if (request.SpaceId == null || request.Folder == null)
            throw new InvalidDataException("Invalid upload destination.");
        SharedSpace? targetSpace = request.SpaceId.Length == 0 ? null : Spaces.Get(request.SpaceId, true);
        string requestedFolder = targetSpace == null ? store.Preferences.ReceiveFolder : Spaces.Resolve(targetSpace.Id, request.Folder, true, true);
        if (request.Files == null || request.Files.Count is < 1 or > 100)
            throw new InvalidDataException("Choose between 1 and 100 files.");
        long total = 0;
        Manifest manifest = new()
        {
            Id = Guid.NewGuid().ToString("N")
        };
        foreach (BrowserUploadFile file in request.Files)
        {
            if (file == null || file.Name == null)
                throw new InvalidDataException("An upload name is missing.");
            FileCatalog.ValidateSegment(file.Name);
            if (file.Size < 0 || file.Size > MaxUploadBytes || (total = checked(total + file.Size)) > 10L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Browser uploads support 2 GiB per file and 10 GiB per selection.");
            manifest.Entries.Add(new ManifestEntry { RelativePath = file.Name, Size = file.Size });
        }
        lock (gate)
        {
            if (busySessions.Count >= 8 || !busySessions.Add(session.Id))
                throw new IOException("An upload is already pending. Finish or cancel it first.");
        }
        bool reserved = true;
        Offer? created = null;
        TransferJob job = transfers.Add(new()
        {
            Id = manifest.Id,
            Direction = "Received",
            PeerName = "Phone / browser",
            PeerFingerprint = "browser",
            PeerAddress = session.Address,
            Transport = "Browser · HTTP",
            State = TransferState.AwaitingApproval,
            SpaceId = targetSpace?.Id ?? ""
        });
        TrackTransfer(job, session.Id);
        job.Initialize(manifest);
        using CancellationTokenSource approval = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token, job.Cancellation.Token);
        approval.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            string folder = requestedFolder;
            if (FileCatalog.FreeSpace(folder) < total + 64L * 1024 * 1024)
                throw new IOException("The destination does not have enough free space.");
            ReceiveDecision decision = ApproveReceive == null ? new(false, folder) : await ApproveReceive(new(targetSpace == null ? "Phone / browser" : "Browser → " + targetSpace.Name, "", manifest, folder, FileCatalog.FreeSpace(folder), false, true, targetSpace != null), approval.Token);
            if (!decision.Accepted)
            {
                job.State(TransferState.Declined, "Declined by the PC.");
                context.Response.StatusCode = 403;
                await Json(context, new
                {
                    error = "The PC declined these files."
                });
                return;
            }
            approval.Token.ThrowIfCancellationRequested();
            if (GetSession(context)?.Id != session.Id)
                throw new OperationCanceledException();
            if (targetSpace != null)
                Spaces.CheckAccess(targetSpace.Id, targetSpace.Folder, true);
            string destinationFolder = targetSpace == null ? decision.Folder : requestedFolder;
            FileCatalog.CheckSafeDirectory(destinationFolder);
            if (FileCatalog.FreeSpace(destinationFolder) < total + 64L * 1024 * 1024)
                throw new IOException("The destination does not have enough free space.");
            Directory.CreateDirectory(destinationFolder);
            Offer offer = new(RandomToken(), session.Id, Path.GetFullPath(destinationFolder), manifest, job, targetSpace?.Id ?? "", targetSpace?.Folder ?? "");
            created = offer;
            offers[offer.Id] = offer;
            reserved = false;
            job.Update(record => record with { Destination = offer.Folder, State = TransferState.Receiving });
            await Json(context, new
            {
                id = offer.Id,
                count = manifest.FileCount
            });
        }
        catch (Exception error)
        {
            if (created != null)
            {
                created.Cancellation.Cancel();
                RemoveOffer(created);
            }
            if (!job.Snapshot.Terminal)
                job.State(error is OperationCanceledException ? TransferState.Cancelled : TransferState.Failed, TransferEngine.HumanError(error));
            throw;
        }
        finally { if (reserved) lock (gate) busySessions.Remove(session.Id); }
    }
    private async Task UploadAsync(HttpContext context, Offer offer, int index)
    {
        ManifestEntry entry = offer.Manifest.Entries[index];
        if (Interlocked.CompareExchange(ref offer.InFlight, 1, 0) != 0)
            throw new IOException("Finish the current file before sending another.");
        if (offer.ExpiresUtc < DateTime.UtcNow || context.Request.ContentLength != entry.Size || Interlocked.CompareExchange(ref offer.States[index], 1, 0) != 0)
        {
            offer.InFlight = 0;
            throw new InvalidDataException("This upload expired, was already sent, or has the wrong size.");
        }
        if (!await streams.WaitAsync(0, context.RequestAborted))
        {
            offer.States[index] = 0;
            offer.InFlight = 0;
            throw new IOException("Three files are already moving. Try again shortly.");
        }
        string temporary = Path.Combine(offer.Folder, ".flowshare-" + Guid.NewGuid().ToString("N") + ".part");
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token, offer.Cancellation.Token, offer.Job.Cancellation.Token);
        try
        {
            if (offer.SpaceId.Length > 0)
                Spaces.CheckAccess(offer.SpaceId, offer.SpaceRoot, true);
            FileCatalog.CheckSafeDirectory(offer.Folder);
            if (FileCatalog.FreeSpace(offer.Folder) < entry.Size + 64L * 1024 * 1024)
                throw new IOException("The destination does not have enough free space.");
            offer.Job.StartFile(entry, TransferState.Receiving);
            StartTransfer(offer.Job.Snapshot.Id);
            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                long remaining = entry.Size;
                byte[] buffer = new byte[65536];
                while (remaining > 0)
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    int read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token);
                    if (read == 0)
                        throw new EndOfStreamException("The browser disconnected before the file finished.");
                    await output.WriteAsync(buffer.AsMemory(0, read), work.Token);
                    digest.AppendData(buffer, 0, read);
                    remaining -= read;
                    offer.Job.Advance(read);
                }
                await output.FlushAsync(work.Token);
                output.Flush(true);
            }
            work.Token.ThrowIfCancellationRequested();
            if (offer.SpaceId.Length > 0)
                Spaces.CheckAccess(offer.SpaceId, offer.SpaceRoot, true);
            FileCatalog.CheckSafeDirectory(offer.Folder);
            string destination = KeepBoth(temporary, offer.Folder, entry.RelativePath);
            offer.States[index] = 2;
            offer.Job.Update(record => record with { Files = record.Files.Select((file, at) => at == index ? file with { Status = "Saved · SHA-256 recorded", Bytes = entry.Size } : file).ToList() });
            if (offer.States.All(state => state == 2))
            {
                offer.Job.State(TransferState.Completed);
                RemoveOffer(offer);
            }
            await Json(context, new
            {
                saved = true,
                name = Path.GetFileName(destination),
                sha256 = Convert.ToHexString(digest.GetHashAndReset())
            });
        }
        catch (Exception error)
        {
            bool cancelled = work.IsCancellationRequested;
            offer.Cancellation.Cancel();
            RemoveOffer(offer);
            if (!offer.Job.Snapshot.Terminal)
                offer.Job.State(cancelled ? TransferState.Cancelled : TransferState.Failed, TransferEngine.HumanError(error));
            throw;
        }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } offer.InFlight = 0; streams.Release(); }
    }
    private static string KeepBoth(string temporary, string folder, string name)
    {
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            string candidate = suffix == 0 ? name : Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name);
            string destination = FileCatalog.Within(folder, candidate);
            FileCatalog.CheckSafePath(destination);
            try
            {
                File.Move(temporary, destination, false);
                return destination;
            }
            catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
        }
        throw new IOException("Too many duplicate filenames. Rename this file and try again.");
    }
    private async Task DownloadAsync(HttpContext context, string id, Session session)
    {
        BrowserFile? selected;
        lock (gate)
            files.TryGetValue(id, out selected);
        if (selected == null)
        {
            context.Response.StatusCode = 404;
            return;
        }
        await DownloadFileAsync(context, selected, session);
    }
    private async Task DownloadFileAsync(HttpContext context, BrowserFile selected, Session session, SharedSpace? space = null)
    {
        if (!await streams.WaitAsync(0, context.RequestAborted))
            throw new IOException("Three files are already moving. Try again shortly.");
        TransferJob? job = null;
        try
        {
            FileCatalog.CheckSafePath(selected.Path);
            await using FileStream source = new(selected.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != selected.Size || File.GetLastWriteTimeUtc(selected.Path) != selected.ModifiedUtc)
                throw new IOException("This file changed. Share it again from the PC.");
            ManifestEntry entry = new()
            {
                RelativePath = Path.GetFileName(selected.Name),
                Size = source.Length
            };
            Manifest manifest = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Entries = [entry]
            };
            job = transfers.Add(new()
            {
                Id = manifest.Id,
                Direction = "Sent",
                PeerName = "Phone / browser",
                PeerFingerprint = "browser",
                PeerAddress = session.Address,
                Transport = "Browser · HTTP",
                State = TransferState.Sending,
                SpaceId = space?.Id ?? ""
            });
            TrackTransfer(job, session.Id);
            StartTransfer(job.Snapshot.Id);
            job.Initialize(manifest);
            job.StartFile(entry, TransferState.Sending);
            CancellationToken access;
            lock (gate)
            {
                if (session.Generation != generation)
                    throw new OperationCanceledException();
                access = accessLifetime.Token;
            }
            using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token, access, job.Cancellation.Token, space == null ? CancellationToken.None : SpaceToken(space.Id));
            if (space != null)
                Spaces.CheckAccess(space.Id, space.Folder, false);
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = source.Length;
            context.Response.Headers.ContentDisposition = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(entry.RelativePath);
            byte[] buffer = new byte[65536];
            int read;
            while ((read = await source.ReadAsync(buffer, work.Token)) > 0)
            {
                if (space != null)
                    Spaces.CheckAccess(space.Id, space.Folder, false);
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), work.Token);
                job.Advance(read);
            }
            await context.Response.Body.FlushAsync(work.Token);
            job.Update(record => record with { Files = [new TransferFile { RelativePath = entry.RelativePath, Size = entry.Size, Bytes = entry.Size, Status = "Sent to browser" }] });
            job.State(TransferState.Completed);
        }
        catch (Exception error) { if (job != null && !job.Snapshot.Terminal) job.State(context.RequestAborted.IsCancellationRequested || lifetime.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, TransferEngine.HumanError(error)); throw; }
        finally { streams.Release(); }
    }
    private void RemoveOffer(Offer offer)
    {
        offers.TryRemove(offer.Id, out _);
        lock (gate)
            busySessions.Remove(offer.Session);
    }
    public void Dispose()
    {
        Stop();
        WhenStopped.GetAwaiter().GetResult();
    }
}
