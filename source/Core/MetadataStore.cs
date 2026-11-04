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
            // A read-only main or backup file would otherwise stop every save, and with it startup.
            foreach (string existing in new[] { filename, filename + ".bak" })
                if (File.Exists(existing) && File.GetAttributes(existing).HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(existing, File.GetAttributes(existing) & ~FileAttributes.ReadOnly);
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
        catch (IOException error) when (Locked(error)) { throw new IOException("metadata.json is in use by another program. Close that program and start hopo again; nothing was changed.", error); }
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
        // A missing main file next to a saved copy is recovered from that copy, never replaced by a new identity.
        if (!File.Exists(path))
            return !path.EndsWith(".bak", StringComparison.Ordinal) && File.Exists(path + ".bak") ? throw new FileNotFoundException("metadata.json is missing.") : new();
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("The metadata file exceeds the supported size.");
        Document doc = JsonSerializer.Deserialize<Document>(ReadAll(path), Json) ?? throw new InvalidDataException("Metadata is empty.");
        if (doc.Version != 1)
            throw new InvalidDataException("This metadata version requires a newer Lantoss app.");
        // A tampered or damaged file must not smuggle in records the app would refuse to save.
        doc.Peers = (doc.Peers ?? []).Where(p => p is { Fingerprint.Length: 64, Name: not null, Address: not null } && p.Fingerprint.All(char.IsAsciiHexDigit) && Valid(p.Name)).DistinctBy(p => p.Fingerprint).TakeLast(256).ToList();
        doc.Spaces = (doc.Spaces ?? []).Where(s => s is { Name: not null, Folder: not null } && Guid.TryParseExact(s.Id, "N", out _) && Enum.IsDefined(s.Access) && Valid(s.Name) && Path.IsPathFullyQualified(s.Folder)).DistinctBy(s => s.Id).Take(32).ToList();
        doc.History = (doc.History ?? []).Where(h => h is { Id: not null, Direction: not null }).Select(h => h with { PeerName = h.PeerName ?? "Unknown device", PeerFingerprint = h.PeerFingerprint ?? "", Title = h.Title ?? "Files", Files = h.Files ?? [], Sources = h.Sources ?? [], CurrentFile = h.CurrentFile ?? "", Error = h.Error ?? "", Destination = h.Destination ?? "", PeerAddress = h.PeerAddress ?? "", SpaceId = h.SpaceId ?? "", Transport = h.Transport ?? "" }).DistinctBy(h => h.Id + h.Direction).Take(300).ToList();
        doc.Preferences ??= new();
        // Keep only well-formed trust entries, newest 64.
        doc.Preferences = doc.Preferences with
        {
            TrustedBrowsers = (doc.Preferences.TrustedBrowsers ?? []).Where(t => t is { TokenHash.Length: 64, Label: not null } && t.TokenHash.All(char.IsAsciiHexDigit) && t.Label.Length <= 200).OrderByDescending(t => t.AddedUtc).Take(64).ToArray()
        };
        // A bad field falls back on its own; the rest of the saved settings are kept.
        Preferences defaults = new(), loaded = doc.Preferences;
        if (!Valid(loaded.DeviceName ?? ""))
            loaded = loaded with
            {
                DeviceName = defaults.DeviceName
            };
        if (loaded.Theme is not ("Light" or "Dark" or "System"))
            loaded = loaded with
            {
                Theme = defaults.Theme
            };
        if (string.IsNullOrEmpty(loaded.ReceiveFolder) || !Path.IsPathFullyQualified(loaded.ReceiveFolder))
            loaded = loaded with
            {
                ReceiveFolder = defaults.ReceiveFolder
            };
        try
        {
            loaded = loaded with
            {
                LocalName = Profiles.AddressName(loaded.LocalName)
            };
        }
        catch (InvalidDataException) { loaded = loaded with { LocalName = defaults.LocalName }; }
        doc.Preferences = loaded;
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
    private static bool Locked(Exception error) => error is IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) };
    /// <summary>Another program (often a virus scanner) may hold the file for a moment; wait for it rather than treating it as damaged.</summary>
    private static byte[] ReadAll(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException error) when (Locked(error) && attempt < 25) { Thread.Sleep(200); }
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
        CheckReceiveFolder(preferences.ReceiveFolder);
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
    /// <summary>Refuses receive folders that would only fail when a file arrives: a missing or network drive, a drive root, a system folder or a very long path.</summary>
    public static void CheckReceiveFolder(string folder)
    {
        string full = Path.GetFullPath(folder), root = Path.GetPathRoot(full) ?? "";
        FileCatalog.CheckSafeDirectory(full);
        if (full.Length > 200)
            throw new InvalidDataException("Choose a receive folder with a shorter path, up to 200 characters.");
        if (root.Length == 0 || !Directory.Exists(root))
            throw new InvalidDataException("That drive is not available. Connect it or choose another receive folder.");
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a folder on the drive, not the whole drive.");
        if (OperatingSystem.IsWindows() && new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidDataException("Use a local drive. Mapped network drives are not supported as the receive folder.");
        foreach (Environment.SpecialFolder system in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            if (Environment.GetFolderPath(system) is { Length: > 0 } blocked && (full + Path.DirectorySeparatorChar).StartsWith(blocked.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a receive folder outside Windows and Program Files.");
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
    /// <summary>Records that a paired PC answered, changing nothing the user controls.</summary>
    public void TouchPeer(string fingerprint, string address, int port, string browserUrl)
    {
        lock (gate)
        {
            int at = data.Peers.FindIndex(p => p.Fingerprint == fingerprint && p.Paired && !p.Blocked);
            if (at < 0)
                return;
            data.Peers[at] = data.Peers[at] with
            {
                Address = address,
                Port = port,
                BrowserUrl = browserUrl,
                LastSeenUtc = DateTime.UtcNow
            };
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
        // Sizing happens before the lock, so UI reads never wait for it. Newest records are kept first, each with at most
        // 200 file rows, until the history budget is used; the saved file therefore always stays far below the load limit.
        List<TransferRecord> kept = [];
        long budget = 12L * 1024 * 1024;
        foreach (TransferRecord record in records.OrderByDescending(r => r.CreatedUtc).Take(300))
        {
            TransferRecord slim = record.Files.Count > HistoryFiles ? record with
            {
                Files = record.Files.Take(HistoryFiles).ToList()
            } : record;
            long size = JsonSerializer.SerializeToUtf8Bytes(slim, Json).LongLength;
            if (size > budget / 8)
                size = JsonSerializer.SerializeToUtf8Bytes(slim = slim with
                {
                    Files = [],
                    Sources = []
                }, Json).LongLength;
            if ((budget -= size) < 0)
                break;
            kept.Add(slim);
        }
        lock (gate)
            data.History = kept;
        Save();
    }
    /// <summary>File rows kept per history record; the pages show at most 200 and the counts stay exact.</summary>
    public const int HistoryFiles = 200;
    private void Save()
    {
        lock (writeGate)
        {
            Document snapshot;
            lock (gate)
                snapshot = data with
                {
                    Peers = [.. data.Peers],
                    History = [.. data.History],
                    Spaces = [.. data.Spaces]
                };
            // Serialising outside the state lock keeps the UI responsive while a large history is written.
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
            // Disk flushes never hold the lock used by UI/state readers.
            FileCatalog.CheckSafeDirectory(Root);
            writer.Write(filename, bytes);
        }
    }
}
