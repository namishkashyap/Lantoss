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
using System.Security.Cryptography.X509Certificates;

// Local portal for phones and browsers. HTTPS with the PC's own certificate encrypts it (HTTP only redirects); same-origin, Host, session, approval and CSRF checks restrict requests.
namespace Lantoss.Networking;

/// <summary>Recipient empty: every approved browser sees the file. Otherwise only the browser with that handle.</summary>
public sealed record BrowserFile(string Id, string Name, long Size, string Path, DateTime ModifiedUtc, string Recipient = "");
public sealed record BrowserUploadFile(string Name, long Size);
public sealed record BrowserUploadRequest(List<BrowserUploadFile> Files, string SpaceId = "", string Folder = "");
public sealed record BrowserLogin(string Code);
public sealed record BrowserNote(string Content);
public sealed record BrowserProfile(string? Name, string? Picture);
/// <summary>A connected browser as shown in the PC's device list. Name and picture are chosen by the visitor and are not authenticated.</summary>
public sealed record BrowserDevice(string Handle, string Name, string Device, string Address, DateTime ConnectedUtc, DateTime LastSeenUtc, byte[]? Picture, bool Approved = true)
{
    public string Label => Name.Length > 0 ? Name + " (" + Device + ")" : Device;
}

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
    private readonly Dictionary<string, (DateTime Start, int Count)> noteAttempts = [];
    private readonly ConcurrentDictionary<string, Offer> offers = new();
    // Limits are per address as well as global, so one device cannot hold every slot and lock out the others.
    public const int AddressConnections = 8, AddressStreams = 2, AddressOffers = 2, NotesPerWindow = 20;
    private readonly SemaphoreSlim streams = new(6, 6);
    private readonly ConcurrentDictionary<string, int> addressConnections = new(), addressStreams = new();
    private readonly Dictionary<string, DateTime> cooldowns = [];
    private readonly Dictionary<string, (DateTime Start, int Count)> declines = [];
    private readonly HashSet<string> blocked = [];
    private Timer? sweeper;
    private X509Certificate2? certificate;
    private WebApplication? server;
    private NetworkAdapter? adapter;
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource accessLifetime = new();
    private bool protectedMode;
    private readonly string deviceKey;
    private LocalNameService? localNames;
    private int generation;
    private sealed class Session(string id, string csrf, string address, DateTime createdUtc, int generation, string device)
    {
        public string Id { get; } = id;
        public string Csrf { get; } = csrf;
        public string Address { get; } = address;
        public DateTime CreatedUtc { get; } = createdUtc;
        public int Generation { get; } = generation;
        public string Device { get; } = device;
        public string Handle { get; } = RandomToken()[..16];
        public string Name { get; set; } = "";
        public byte[]? Picture
        {
            get; set;
        }
        public DateTime LastSeenUtc { get; set; } = createdUtc;
        public string Label => Name.Length > 0 ? Name + " (" + Device + ")" : Device;
        /// <summary>False until the PC allows this browser (or it entered the access code); only the session and profile routes work before that.</summary>
        public bool Approved
        {
            get; set;
        }
        public CancellationTokenSource Cancellation { get; } = new();
    }
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
    /// <summary>Raised once when a new browser waits for Allow on this PC.</summary>
    public event Action<BrowserDevice>? ApprovalRequested;
    /// <summary>Plain-HTTP address; with HTTPS on it only redirects to <see cref="IpUrl"/>.</summary>
    public string HttpUrl { get; private set; } = "";
    public bool Https => IpUrl.StartsWith("https:", StringComparison.Ordinal);
    /// <summary>SHA-256 of the phone page certificate, shown on the PC so a visitor can compare it with the browser's certificate details.</summary>
    public string CertificateFingerprint => certificate == null ? "" : Convert.ToHexString(SHA256.HashData(certificate.RawData));
    public int[] Ports => new[] { IpUrl, HttpUrl }.Where(url => url.Length > 0).Select(url => new Uri(url).Port).Distinct().ToArray();
    private string CookieName => Https ? "__Host-LantossSession" : "LantossSession";
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
        bool https = store.Preferences.BrowserHttps;
        if (https)
            certificate ??= PortalCertificate(network);
        // HTTPS listens next to the HTTP port; 443 gives the shortest link, 48588 is the fallback beside the native port range.
        (int Http, int Https)[] choices = port == 80 ? [(80, 443), (48586, 48588), (0, 0)] : port == 0 ? [(0, 0)] : [(port, port + 2), (0, 0)];
        for (int index = 0; index < choices.Length; index++)
        {
            candidate = CreateServer(network, choices[index].Http, https ? choices[index].Https : null);
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
        string[] addresses = candidate.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(a => a.TrimEnd('/')).ToArray();
        HttpUrl = addresses.Single(a => a.StartsWith("http:", StringComparison.Ordinal));
        IpUrl = addresses.FirstOrDefault(a => a.StartsWith("https:", StringComparison.Ordinal)) ?? HttpUrl;
        sweeper?.Dispose();
        sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        RefreshLocalAddress();
        Changed?.Invoke();
    }
    /// <summary>The phone page certificate is created once per PC (so a phone confirms it once) and kept in a user-only file in the data folder.</summary>
    private X509Certificate2 PortalCertificate(NetworkAdapter network)
    {
        X509KeyStorageFlags flags = OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet;
        string path = Path.Combine(store.Root, "phone-page.p12");
        if (!allowLoopback && File.Exists(path))
        {
            try
            {
                X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(path, "", flags);
                if (existing.HasPrivateKey && existing.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30))
                    return existing;
                existing.Dispose();
            }
            catch (CryptographicException) { }
        }
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=Lantoss phone page, O=" + new string(store.Preferences.DeviceName.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').Take(48).ToArray()), key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddIpAddress(network.Address);
        names.AddDnsName(LocalNameClaim.SharedHost);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        // 800 days stays inside Apple's 825-day limit for TLS server certificates.
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(800));
        byte[] pfx = created.Export(X509ContentType.Pkcs12, "");
        try
        {
            if (!allowLoopback)
            {
                File.Delete(path + ".tmp");
                FileStreamOptions options = new()
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream file = new(path + ".tmp", options))
                    file.Write(pfx);
                File.Move(path + ".tmp", path, true);
            }
            return X509CertificateLoader.LoadPkcs12(pfx, "", flags);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    /// <summary>Clears expired offers and cooldowns every minute, so an approved offer that is never uploaded cannot hold a slot.</summary>
    private void Sweep()
    {
        foreach (Offer expired in offers.Values.Where(o => o.ExpiresUtc < DateTime.UtcNow))
        {
            expired.Cancellation.Cancel();
            expired.Job.Cancel();
            RemoveOffer(expired);
        }
        lock (gate)
        {
            foreach (string old in cooldowns.Where(c => c.Value < DateTime.UtcNow).Select(c => c.Key).ToArray())
                cooldowns.Remove(old);
            foreach (string old in declines.Where(d => DateTime.UtcNow - d.Value.Start >= TimeSpan.FromMinutes(2)).Select(d => d.Key).ToArray())
                declines.Remove(old);
            foreach (string old in noteAttempts.Where(n => DateTime.UtcNow - n.Value.Start >= TimeSpan.FromMinutes(10)).Select(n => n.Key).ToArray())
                noteAttempts.Remove(old);
        }
    }
    public void RefreshLocalAddress()
    {
        if (!Running || adapter == null)
            return;
        // Names are published on any port (port 80 gives the shortest link), using the admin's chosen address.
        int port = new Uri(IpUrl).Port;
        string alias;
        try
        {
            alias = Profiles.AddressName(store.Preferences.LocalName) + ".local";
        }
        catch (InvalidDataException) { alias = LocalNameClaim.SharedHost; }
        bool wanted = !allowLoopback && store.Preferences.Discoverable;
        if (wanted && localNames != null && localNames.Alias == alias && localNames.Port == port)
            return;
        string before = Url;
        localNames?.Dispose();
        localNames = null;
        if (wanted)
        {
            LocalNameService names = new(adapter, deviceKey, alias: alias, port: port);
            names.Changed += () =>
            {
                if (!ReferenceEquals(localNames, names))
                    return;
                LocalAddressStatus = names.DeviceUrl.Length > 0
                    ? names.Url.Contains(alias, StringComparison.Ordinal) ? "Name ready. iPhone, iPad and computers can type it; the QR code works on every phone." : "Another PC already uses " + alias + ". This PC answers at its own name; choose a different address in Settings."
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
            : "PC discovery is off. Enable it in Settings for the local name; the QR code still works.";
        if (before != Url)
            Changed?.Invoke();
    }
    private WebApplication CreateServer(NetworkAdapter network, int port, int? httpsPort)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(BrowserPortal).Assembly.FullName, ContentRootPath = store.Root, EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(network.Address, port, listen => listen.Use(Admit));
            if (httpsPort is int secure)
                options.Listen(network.Address, secure, listen =>
                {
                    listen.Use(Admit);
                    listen.UseHttps(new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions { ServerCertificate = certificate, SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13 });
                });
            options.Limits.MaxConcurrentConnections = 64;
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
    /// <summary>Runs before TLS and HTTP parsing: blocked addresses and an address holding too many connections are dropped at once.</summary>
    private Microsoft.AspNetCore.Connections.ConnectionDelegate Admit(Microsoft.AspNetCore.Connections.ConnectionDelegate next) => async connection =>
    {
        string address = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
        bool refused;
        lock (gate)
            refused = blocked.Contains(address);
        if (refused || addressConnections.AddOrUpdate(address, 1, (_, count) => count + 1) > AddressConnections)
        {
            if (!refused)
                Release(addressConnections, address);
            connection.Abort();
            return;
        }
        try
        {
            await next(connection);
        }
        finally { Release(addressConnections, address); }
    };
    private static void Release(ConcurrentDictionary<string, int> counts, string key)
    {
        if (counts.AddOrUpdate(key, 0, (_, count) => count - 1) <= 0)
            counts.TryRemove(new KeyValuePair<string, int>(key, 0));
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
            foreach (Session session in sessions.Values)
                session.Cancellation.Cancel();
            sessions.Clear();
            noteAttempts.Clear();
            cooldowns.Clear();
            declines.Clear();
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
        HttpUrl = "";
        sweeper?.Dispose();
        sweeper = null;
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
    /// <summary>Offers files to every approved browser, or only to the browser with <paramref name="recipient"/> as its handle.</summary>
    public async Task ShareAsync(IEnumerable<string> paths, CancellationToken token = default, string recipient = "")
    {
        Manifest selected = await FileCatalog.BuildAsync(paths, Guid.NewGuid().ToString("N"), false, token);
        lock (gate)
        {
            if (recipient.Length > 0 && !sessions.Values.Any(s => s.Handle == recipient && s.Approved))
                throw new IOException("That phone or browser is no longer connected. Ask it to open the page again.");
            if (files.Count + selected.FileCount > 1000)
                throw new IOException("Share up to 1,000 browser files at a time.");
            foreach (ManifestEntry entry in selected.Entries.Where(e => !e.Directory))
            {
                if (files.Values.Any(f => f.Recipient == recipient && f.Path.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string id = Guid.NewGuid().ToString("N");
                files[id] = new(id, entry.RelativePath, entry.Size, entry.SourcePath, entry.SourceModifiedUtc, recipient);
            }
        }
        Changed?.Invoke();
    }
    private bool Visible(BrowserFile file, Session session) => file.Recipient.Length == 0 || file.Recipient == session.Handle;
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
        string? key = context.Request.Cookies[CookieName];
        lock (gate)
        {
            if (key != null && sessions.TryGetValue(key, out Session? found) && found.Generation == generation && found.Address == context.Connection.RemoteIpAddress?.ToString() && DateTime.UtcNow - found.CreatedUtc < TimeSpan.FromHours(12) && !blocked.Contains(found.Address))
            {
                found.LastSeenUtc = DateTime.UtcNow;
                return found;
            }
        }
        return null;
    }
    private Session CreateSession(HttpContext context, bool approved)
    {
        Session session;
        lock (gate)
        {
            foreach (string old in sessions.Where(s => DateTime.UtcNow - s.Value.CreatedUtc >= TimeSpan.FromHours(12)).Select(s => s.Key).ToArray())
                sessions.Remove(old);
            string address = context.Connection.RemoteIpAddress!.ToString();
            // One address cannot fill the table and lock out other phones: it replaces its own oldest session instead.
            Session[] own = sessions.Values.Where(s => s.Address == address).OrderBy(s => s.LastSeenUtc).ToArray();
            if (own.Length >= 4)
                End(own[0]);
            // A full table gives up the least recently seen browser, waiting ones first, instead of refusing every new phone.
            if (sessions.Count >= 64)
                End(sessions.Values.OrderBy(s => s.Approved).ThenBy(s => s.LastSeenUtc).First());
            session = new(RandomToken(), RandomToken(), address, DateTime.UtcNow, generation, Profiles.DeviceLabel(context.Request.Headers.UserAgent.ToString()))
            {
                Approved = approved
            };
            sessions[session.Id] = session;
            context.Response.Cookies.Append(CookieName, session.Id, new CookieOptions { HttpOnly = true, Secure = Https, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(12), IsEssential = true });
        }
        if (!approved)
            ApprovalRequested?.Invoke(Device(session));
        Changed?.Invoke();
        return session;
        void End(Session old)
        {
            old.Cancellation.Cancel();
            sessions.Remove(old.Id);
        }
    }
    private static BrowserDevice Device(Session s) => new(s.Handle, s.Name, s.Device, s.Address, s.CreatedUtc, s.LastSeenUtc, s.Picture, s.Approved);
    public BrowserDevice[] Devices
    {
        get
        {
            lock (gate)
                return sessions.Values.Where(s => s.Generation == generation && DateTime.UtcNow - s.CreatedUtc < TimeSpan.FromHours(12)).OrderByDescending(s => s.LastSeenUtc).Select(Device).ToArray();
        }
    }
    /// <summary>Lets a waiting browser open files, spaces and notes.</summary>
    public void Approve(string handle)
    {
        lock (gate)
        {
            Session? waiting = sessions.Values.FirstOrDefault(s => s.Handle == handle);
            if (waiting == null)
                return;
            waiting.Approved = true;
        }
        Changed?.Invoke();
    }
    /// <summary>Disconnects the browser and refuses its address until Lantoss restarts or the block is lifted.</summary>
    public void Block(string handle)
    {
        lock (gate)
        {
            if (sessions.Values.FirstOrDefault(s => s.Handle == handle) is not Session found)
                return;
            blocked.Add(found.Address);
        }
        Disconnect(handle);
    }
    public string[] BlockedAddresses
    {
        get
        {
            lock (gate)
                return blocked.Order().ToArray();
        }
    }
    public void Unblock(string address)
    {
        lock (gate)
            blocked.Remove(address);
        Changed?.Invoke();
    }
    /// <summary>Ends one browser's session and cancels its pending uploads and downloads; the page must connect again.</summary>
    public void Disconnect(string handle)
    {
        Session? removed;
        lock (gate)
        {
            removed = sessions.Values.FirstOrDefault(s => s.Handle == handle);
            if (removed == null)
                return;
            removed.Cancellation.Cancel();
            sessions.Remove(removed.Id);
            busySessions.Remove(removed.Id);
            foreach (string file in files.Values.Where(f => f.Recipient == handle).Select(f => f.Id).ToArray())
                files.Remove(file);
        }
        foreach (Offer offer in offers.Values.Where(o => o.Session == removed.Id))
        {
            offer.Cancellation.Cancel();
            offer.Job.Cancel();
            RemoveOffer(offer);
        }
        Changed?.Invoke();
    }
    private bool SameOrigin(HttpContext context)
    {
        string origin = context.Request.Headers.Origin.ToString();
        return LocalBrowserAccess.SameOrigin(origin, context.Request.Host.Host, context.Request.Host.Port, context.Request.Headers["Sec-Fetch-Site"].ToString(), context.Request.Scheme);
    }
    private bool AllowedHost(HttpContext context) => LocalBrowserAccess.HostAllowed(context.Request.IsHttps ? IpUrl : HttpUrl, context.Request.Host.Host, context.Request.Host.Port, name => localNames?.Accepts(name) == true);
    private static bool Csrf(HttpContext context, Session session) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(context.Request.Headers["X-Lantoss-CSRF"].ToString()), Encoding.ASCII.GetBytes(session.Csrf));
    private async Task HandleAsync(HttpContext context)
    {
        try
        {
            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (adapter == null || remote == null || !adapter.Contains(remote) || (!NetworkAdapter.Private(remote) && !(allowLoopback && IPAddress.IsLoopback(remote))) || !AllowedHost(context))
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=()";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'self'; require-trusted-types-for 'script'; trusted-types 'none'";
            string path = context.Request.Path.Value ?? "/";
            bool get = HttpMethods.IsGet(context.Request.Method);
            // HSTS is deliberately not sent: with the PC's own certificate it would turn the one-time browser warning into a hard block.
            // Any LAN device can claim a .local name, and plain HTTP is readable on the Wi-Fi, so both only redirect: the page, its cookies and the API live on the encrypted IP origin.
            if (!Uri.TryCreate(IpUrl, UriKind.Absolute, out Uri? ipOrigin) || (Https && !context.Request.IsHttps) || !context.Request.Host.Host.TrimEnd('.').Equals(ipOrigin.Host, StringComparison.OrdinalIgnoreCase))
            {
                if (get && path == "/")
                    context.Response.Redirect(IpUrl + "/");
                else
                    context.Response.StatusCode = 403;
                return;
            }
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
                    session = CreateSession(context, !store.Preferences.ApproveNewDevices);
                await Json(context, new
                {
                    connected = session?.Approved == true,
                    waiting = session is { Approved: false },
                    requiresCode = store.Preferences.RequireConnectionCode,
                    csrf = session?.Csrf,
                    name = store.Preferences.DeviceName,
                    hasPicture = Profiles.Picture(store) != null,
                    profileName = session?.Name ?? "",
                    encrypted = context.Request.IsHttps,
                    certificate = CertificateFingerprint.Length > 0 ? CertificateFingerprint[..8] + " " + CertificateFingerprint[8..16] : "",
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
                    // Per address only: many addresses cannot lock everyone out, and 5 tries a minute against a 50-bit single-use code is still hopeless.
                    if (attempts.Count >= 4096)
                        attempts.Remove(attempts.MinBy(p => p.Value.Start).Key);
                    if (attempts.TryGetValue(remoteAddress, out var previous) && previous.Count >= 5)
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
                // The access code proves the visitor read this PC's screen, so it also approves the browser.
                session = CreateSession(context, store.Preferences.RequireConnectionCode || !store.Preferences.ApproveNewDevices);
                await Json(context, new
                {
                    connected = session.Approved,
                    waiting = !session.Approved,
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
            // A waiting browser may only show its name and picture to the PC; files, spaces, notes and other PCs stay hidden until Allow.
            if (!session.Approved && path is not ("/api/profile" or "/api/picture"))
            {
                context.Response.StatusCode = 403;
                await Json(context, new
                {
                    error = "Waiting for the PC to allow this device.",
                    waiting = true
                });
                return;
            }
            if (get && path == "/api/picture")
            {
                byte[]? picture = Profiles.Picture(store);
                if (picture == null)
                {
                    context.Response.StatusCode = 404;
                    return;
                }
                context.Response.ContentType = picture[0] == 0x89 ? "image/png" : "image/jpeg";
                await context.Response.Body.WriteAsync(picture, context.RequestAborted);
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method) && path == "/api/profile")
            {
                BrowserProfile profile = await ReadJson<BrowserProfile>(context, Profiles.MaxPictureBytes * 2);
                string name = Profiles.CleanName(profile.Name);
                byte[]? picture = Profiles.FromDataUrl(profile.Picture);
                lock (gate)
                {
                    session.Name = name;
                    session.Picture = picture;
                }
                Changed?.Invoke();
                await Json(context, new
                {
                    saved = true,
                    name
                });
                return;
            }
            if (get && path == "/api/files")
            {
                await Json(context, Files.Where(f => Visible(f, session)).Select(f => new { id = f.Id, name = f.Name, size = f.Size, personal = f.Recipient.Length > 0 }));
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
                bool limited;
                lock (gate)
                {
                    // Each address may post 20 notes per 10 minutes, so one device cannot push everyone else's notes off the board.
                    (DateTime Start, int Count) quota = noteAttempts.GetValueOrDefault(session.Address, (DateTime.UtcNow, 0));
                    if (DateTime.UtcNow - quota.Start >= TimeSpan.FromMinutes(10))
                        quota = (DateTime.UtcNow, 0);
                    limited = quota.Count >= NotesPerWindow;
                    if (!limited)
                        noteAttempts[session.Address] = (quota.Start, quota.Count + 1);
                }
                if (limited)
                {
                    context.Response.StatusCode = 429;
                    await Json(context, new
                    {
                        error = "Too many notes from this device. Try again in a few minutes."
                    });
                    return;
                }
                BrowserNote note = await ReadJson<BrowserNote>(context, 100000);
                SharedNote saved = Notes.Add(note.Content, session.Label);
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
                        error = error is OperationCanceledException ? "The transfer stopped or was declined. Try again when the PC is ready." : PublicError(error)
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
    /// <summary>System messages can contain local paths and the Windows user name; browsers get a generic line for those.</summary>
    public static string PublicError(Exception error)
    {
        string message = TransferEngine.HumanError(error);
        return message.IndexOf((char)92) >= 0 || message.Contains("/Users/", StringComparison.Ordinal) || message.Contains("/home/", StringComparison.Ordinal) || message.Contains("/Volumes/", StringComparison.Ordinal) || message.Contains("/media/", StringComparison.Ordinal)
            ? error is FileNotFoundException or DirectoryNotFoundException ? "That file is no longer available on the PC." : "The PC could not read or save this file. Check Lantoss on the PC." : message;
    }
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
        string requestedFolder = targetSpace != null ? Spaces.Resolve(targetSpace.Id, request.Folder, true, true) : store.Preferences.SenderFolders ? Profiles.SenderFolder(store.Preferences.ReceiveFolder, session.Label) : store.Preferences.ReceiveFolder;
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
            if (cooldowns.TryGetValue(session.Address, out DateTime until) && until > DateTime.UtcNow)
                throw new IOException("The PC declined the last request. Wait a moment before asking again.");
            if (busySessions.Contains(session.Id))
                throw new IOException("An upload is already pending. Finish or cancel it first.");
            if (busySessions.Count >= 16 || busySessions.Count(id => sessions.TryGetValue(id, out Session? other) && other.Address == session.Address) >= AddressOffers)
                throw new IOException("The PC is busy with other requests. Try again shortly.");
            busySessions.Add(session.Id);
        }
        bool reserved = true;
        Offer? created = null;
        TransferJob job = transfers.Add(new()
        {
            Id = manifest.Id,
            Direction = "Received",
            PeerName = session.Label,
            PeerFingerprint = "browser",
            PeerAddress = session.Address,
            Transport = Https ? "Browser · HTTPS" : "Browser · HTTP",
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
            ReceiveDecision decision = ApproveReceive == null ? new(false, folder) : await ApproveReceive(new(targetSpace == null ? session.Label : session.Label + " → " + targetSpace.Name, "", manifest, folder, FileCatalog.FreeSpace(folder), false, true, targetSpace != null, session.Address, session.Picture), approval.Token);
            if (!decision.Accepted)
            {
                // Three declines within two minutes start a 30-second cooldown, so a declined device cannot flood the PC with prompts.
                lock (gate)
                {
                    (DateTime Start, int Count) recent = declines.GetValueOrDefault(session.Address, (DateTime.UtcNow, 0));
                    if (DateTime.UtcNow - recent.Start >= TimeSpan.FromMinutes(2))
                        recent = (DateTime.UtcNow, 0);
                    declines[session.Address] = (recent.Start, recent.Count + 1);
                    if (recent.Count + 1 >= 3)
                        cooldowns[session.Address] = DateTime.UtcNow.AddSeconds(30);
                }
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
        if (!TryStream(context))
        {
            offer.States[index] = 0;
            offer.InFlight = 0;
            throw new IOException("Several files are already moving. Try again shortly.");
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
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } offer.InFlight = 0; ReleaseStream(context); }
    }
    private static string KeepBoth(string temporary, string folder, string name)
    {
        name = FileCatalog.Defuse(name);
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            string candidate = suffix == 0 ? name : Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name);
            string destination = FileCatalog.Within(folder, candidate);
            FileCatalog.CheckSafePath(destination);
            try
            {
                File.Move(temporary, destination, false);
                FileCatalog.MarkReceived(destination);
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
        if (selected == null || !Visible(selected, session))
        {
            context.Response.StatusCode = 404;
            return;
        }
        await DownloadFileAsync(context, selected, session);
    }
    private async Task DownloadFileAsync(HttpContext context, BrowserFile selected, Session session, SharedSpace? space = null)
    {
        if (!TryStream(context))
            throw new IOException("Several files are already moving. Try again shortly.");
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
                PeerName = session.Label,
                PeerFingerprint = "browser",
                PeerAddress = session.Address,
                Transport = Https ? "Browser · HTTPS" : "Browser · HTTP",
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
            using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token, access, session.Cancellation.Token, job.Cancellation.Token, space == null ? CancellationToken.None : SpaceToken(space.Id));
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
        finally { ReleaseStream(context); }
    }
    private bool TryStream(HttpContext context)
    {
        if (!streams.Wait(0))
            return false;
        if (addressStreams.AddOrUpdate(context.Connection.RemoteIpAddress?.ToString() ?? "", 1, (_, count) => count + 1) <= AddressStreams)
            return true;
        ReleaseStream(context);
        return false;
    }
    private void ReleaseStream(HttpContext context)
    {
        Release(addressStreams, context.Connection.RemoteIpAddress?.ToString() ?? "");
        streams.Release();
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
        certificate?.Dispose();
    }
}
