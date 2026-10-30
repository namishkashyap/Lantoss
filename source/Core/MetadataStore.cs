using System.Text.Json;

namespace Lantoss.Core;

/// <summary>
/// Durable preferences, paired device keys, space registrations and recent history. Writes use a flushed temporary file and atomic replacement; .bak is the recovery copy.
/// </summary>
public sealed class MetadataStore
{
    public interface IAtomicWriter
    {
        void Write(string filename, byte[] bytes);
    }
    public sealed class AtomicDiskWriter : IAtomicWriter
    {
        public void Write(string filename, byte[] bytes)
        {
            string temp = filename + ".new";
            // A leftover temporary file is replaced, never reused, so it cannot keep wider permissions.
            File.Delete(temp);
            FileStreamOptions options = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (FileStream file = new(temp, options))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            if (File.Exists(filename))
                File.Replace(temp, filename, filename + ".bak");
            else
                File.Move(temp, filename);
        }
    }
    private sealed record Document
    {
        public int Version { get; init; } = 1;
        public string CertificateThumbprint { get; set; } = "";
        public Preferences Preferences { get; set; } = new();
        public List<Peer> Peers { get; set; } = [];
        public List<TransferRecord> History { get; set; } = [];
        public List<SharedSpace> Spaces { get; set; } = [];
    }
    private readonly object gate = new();
    private readonly object writeGate = new();
    private readonly IAtomicWriter writer;
    private readonly string filename;
    private Document data;
    public string? RecoveryNotice
    {
        get;
    }
    public string Root
    {
        get;
    }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = false, MaxDepth = 24 };
    public MetadataStore(string root, IAtomicWriter? writer = null)
    {
        this.writer = writer ?? new AtomicDiskWriter();
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        FileCatalog.CheckSafeDirectory(Root);
        RestrictToUser(Root);
        filename = Path.Combine(Root, "metadata.json");
        try
        {
            data = Load(filename);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            try
            {
                if (!File.Exists(filename + ".bak"))
                    throw new IOException("No saved copy exists.");
                data = Load(filename + ".bak");
                RecoveryNotice = "Recovered metadata from the previous saved copy. " + error.Message;
            }
            catch { throw new InvalidDataException("Lantoss metadata cannot be read. Keep the files in " + Root + " and contact support; no history has been erased.", error); }
        }
        bool interrupted = false;
        data.History = data.History.Select(record =>
        {
            if (record.Terminal)
                return record;
            interrupted = true;
            return record with
            {
                State = TransferState.Failed,
                BytesPerSecond = 0,
                Error = "Lantoss closed before this transfer finished. Retry starts a new transfer.",
                UpdatedUtc = DateTime.UtcNow
            };
        }).ToList();
        if (interrupted)
            Save();
    }
    private static Document Load(string path)
    {
        if (!File.Exists(path))
            return new();
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("The metadata file exceeds the supported size.");
        Document doc = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Metadata is empty.");
        if (doc.Version != 1)
            throw new InvalidDataException("This metadata version requires a newer Lantoss app.");
        // A tampered or damaged file must not smuggle in records the app would refuse to save.
        doc.Peers = (doc.Peers ?? []).Where(p => p is { Fingerprint.Length: 64, Name: not null, Address: not null } && p.Fingerprint.All(char.IsAsciiHexDigit) && Valid(p.Name)).DistinctBy(p => p.Fingerprint).TakeLast(256).ToList();
        doc.Spaces = (doc.Spaces ?? []).Where(s => s is { Name: not null, Folder: not null } && Guid.TryParseExact(s.Id, "N", out _) && Enum.IsDefined(s.Access) && Valid(s.Name) && Path.IsPathFullyQualified(s.Folder)).DistinctBy(s => s.Id).Take(32).ToList();
        doc.History = (doc.History ?? []).Where(h => h != null).Take(300).ToList();
        doc.Preferences ??= new();
        if (!Valid(doc.Preferences.DeviceName ?? "") || doc.Preferences.Theme is not ("Light" or "Dark" or "System") || string.IsNullOrEmpty(doc.Preferences.ReceiveFolder) || !Path.IsPathFullyQualified(doc.Preferences.ReceiveFolder))
            doc.Preferences = new();
        return doc;
        static bool Valid(string name)
        {
            try
            {
                ValidateName(name);
                return true;
            }
            catch (InvalidDataException) { return false; }
        }
    }
    /// <summary>On macOS and Linux the data folder is 0700 and its files 0600, so other local users cannot read history, keys or shared text.</summary>
    public static void RestrictToUser(string root)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                File.SetUnixFileMode(path, Directory.Exists(path) ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public Preferences Preferences
    {
        get
        {
            lock (gate)
                return data.Preferences;
        }
    }
    public string Thumbprint
    {
        get
        {
            lock (gate)
                return data.CertificateThumbprint;
        }
    }
    public Peer[] Peers
    {
        get
        {
            lock (gate)
                return data.Peers.ToArray();
        }
    }
    public TransferRecord[] History
    {
        get
        {
            lock (gate)
                return data.History.OrderByDescending(h => h.CreatedUtc).ToArray();
        }
    }
    public void SetCertificate(string thumbprint)
    {
        lock (gate)
            data.CertificateThumbprint = thumbprint;
        Save();
    }
    public SharedSpace[] Spaces
    {
        get
        {
            lock (gate)
                return data.Spaces.ToArray();
        }
    }
    public void SetSpaces(IEnumerable<SharedSpace> spaces)
    {
        SharedSpace[] selected = spaces.ToArray();
        if (selected.Length > 32 || selected.Select(space => space.Id).Distinct().Count() != selected.Length)
            throw new InvalidDataException("Up to 32 distinct shared folders are supported.");
        foreach (SharedSpace space in selected)
        {
            if (!Guid.TryParseExact(space.Id, "N", out _) || !Enum.IsDefined(space.Access) || space.AllowedPeers is { Length: > 256 } || space.AllowedPeers?.Any(key => key is not { Length: 64 } || !key.All(char.IsAsciiHexDigit)) == true)
                throw new InvalidDataException("Invalid shared-space settings.");
            ValidateName(space.Name);
            if (!Path.IsPathFullyQualified(space.Folder))
                throw new InvalidDataException("Choose a local shared folder.");
        }
        lock (gate)
            data.Spaces = selected.ToList();
        Save();
    }
    public void SetPreferences(Preferences preferences)
    {
        string name = ValidateName(preferences.DeviceName);
        FileCatalog.CheckSafeDirectory(preferences.ReceiveFolder);
        if (preferences.Theme is not ("Light" or "Dark" or "System"))
            throw new InvalidDataException("Choose Light, Dark, or System theme.");
        lock (gate)
            data.Preferences = preferences with
            {
                DeviceName = name,
                LocalName = Profiles.AddressName(preferences.LocalName),
                ReceiveFolder = Path.GetFullPath(preferences.ReceiveFolder)
            };
        Save();
    }
    public static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 64 || name.Any(FileCatalog.Hidden))
            throw new InvalidDataException("Use a device name between 1 and 64 characters without control or hidden formatting characters.");
        return name;
    }
    /// <summary>True when another saved device already shows this name, so approval windows can warn about look-alikes.</summary>
    public bool NameInUse(string name, string fingerprint)
    {
        lock (gate)
            return data.Peers.Any(p => p.Fingerprint != fingerprint && !p.Blocked && string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
    public Peer? Trusted(string fingerprint)
    {
        lock (gate)
            return data.Peers.FirstOrDefault(p => p.Fingerprint == fingerprint && p.Paired && !p.Blocked);
    }
    public bool Blocked(string fingerprint)
    {
        lock (gate)
            return data.Peers.Any(p => p.Fingerprint == fingerprint && p.Blocked);
    }
    public void UpsertPeer(Peer peer)
    {
        lock (gate)
        {
            // The limit is checked before anything changes: a full list keeps working, giving up its oldest unverified, non-favourite record first.
            List<Peer> next = data.Peers.Where(p => p.Fingerprint != peer.Fingerprint).Append(peer).ToList();
            if (next.Count > 256 && next.Where(p => p != peer && !p.KeyVerified && !p.Favorite && !p.Blocked).MinBy(p => p.LastSeenUtc) is Peer oldest)
                next.Remove(oldest);
            if (next.Count > 256)
                throw new InvalidDataException("Up to 256 local device records are supported. Forget unused devices first.");
            data.Peers = next;
        }
        Save();
    }
    public void ForgetPeer(string fingerprint, bool block)
    {
        lock (gate)
        {
            Peer? peer = data.Peers.FirstOrDefault(p => p.Fingerprint == fingerprint);
            data.Peers.RemoveAll(p => p.Fingerprint == fingerprint);
            if (block && peer != null)
                data.Peers.Add(peer with
                {
                    Paired = false,
                    Blocked = true
                });
        }
        Save();
    }
    public void SetHistory(IEnumerable<TransferRecord> records)
    {
        lock (gate)
            data.History = records.OrderByDescending(r => r.CreatedUtc).Take(300).ToList();
        Save();
    }
    private void Save()
    {
        lock (writeGate)
        {
            byte[] bytes;
            lock (gate)
            {
                bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
                while (bytes.Length > 16 * 1024 * 1024 && data.History.Count > 1)
                {
                    data.History.RemoveAt(data.History.Count - 1);
                    bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
                }
            }
            // Disk flushes never hold the lock used by UI/state readers.
            FileCatalog.CheckSafeDirectory(Root);
            writer.Write(filename, bytes);
        }
    }
}
