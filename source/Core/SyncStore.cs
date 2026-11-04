using System.Text.Json;

namespace Lantoss.Core;

// Only host-granted folder access is automatic. No deletion records are applied.
/// <summary>
/// Stores client links, content baselines and host grants separately from UI metadata. A grant binds one sync ID to one paired key and one shared space.
/// </summary>
public sealed class SyncStore
{
    private sealed record Document
    {
        public int Version { get; init; } = 1;
        public List<SyncLink> Links { get; init; } = [];
        public List<SyncGrant> Grants { get; init; } = [];
    }
    private readonly object gate = new();
    private readonly string filename;
    private Document data;
    public event Action? Changed;
    public SyncStore(string root)
    {
        filename = Path.Combine(root, "sync-state.json");
        Document Read(string path)
        {
            if (new FileInfo(path).Length > 24 * 1024 * 1024)
                throw new InvalidDataException("Sync settings are too large.");
            return JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path), MetadataStore.Json) ?? throw new InvalidDataException("Sync settings are empty.");
        }
        Document Checked(string path)
        {
            Document doc = Read(path);
            if (doc.Version > 1)
                throw new NotSupportedException("These sync settings were saved by a newer hopo. Update hopo to use them.");
            if (doc is not { Version: 1, Links: not null, Grants: not null } || doc.Links.Count > 16 || doc.Grants.Count > 64 || doc.Links.Any(link => link is not { Id: not null, Folder: not null, Baseline: not null }) || doc.Grants.Any(grant => grant is null))
                throw new InvalidDataException("Unsupported sync settings.");
            return doc;
        }
        // Damaged sync settings switch sync off for that data only; they never stop the app. The damaged file is kept aside.
        data = new();
        foreach (string path in new[] { filename, filename + ".bak" })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                data = Checked(path);
                if (path != filename)
                    RecoveryNotice = "Sync settings were damaged and were restored from the previous saved copy.";
                return;
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException or NullReferenceException)
            {
                RecoveryNotice = "Sync settings were damaged, so sync links start empty. The damaged file was kept as sync-state.json.damaged.";
                if (path == filename)
                    try
                    {
                        File.Copy(filename, filename + ".damaged", true);
                    }
                    catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    public string? RecoveryNotice
    {
        get; private set;
    }
    public SyncLink[] Links
    {
        get
        {
            lock (gate)
                return data.Links.Select(link => link with { Baseline = new(link.Baseline, StringComparer.OrdinalIgnoreCase) }).ToArray();
        }
    }
    public SyncGrant[] Grants
    {
        get
        {
            lock (gate)
                return data.Grants.ToArray();
        }
    }
    // Hot guards must not clone every file's saved checksum on each transfer block.
    public SyncLink[] Headers
    {
        get
        {
            lock (gate)
                return data.Links.Select(link => link with { Baseline = [] }).ToArray();
        }
    }
    public SyncLink? GetHeader(string id)
    {
        lock (gate)
            return data.Links.FirstOrDefault(link => link.Id == id) is { } link ? link with
            {
                Baseline = []
            } : null;
    }
    public SyncLink? GetLink(string id)
    {
        lock (gate)
            return data.Links.FirstOrDefault(link => link.Id == id) is { } link ? link with
            {
                Baseline = new(link.Baseline, StringComparer.OrdinalIgnoreCase)
            } : null;
    }
    public bool IsEnabled(string id, string? folder = null)
    {
        lock (gate)
        {
            foreach (SyncLink link in data.Links)
                if (link.Id == id && link.Enabled && (folder == null || link.Folder == folder))
                    return true;
            return false;
        }
    }
    public DateTime? LastSuccess(string id)
    {
        lock (gate)
            return data.Links.FirstOrDefault(link => link.Id == id)?.LastSuccessUtc;
    }
    public bool Granted(string id, string key, string space)
    {
        lock (gate)
        {
            foreach (SyncGrant grant in data.Grants)
                if (grant.Id == id && grant.PeerKey == key && grant.SpaceId == space)
                    return true;
            return false;
        }
    }
    public void Grant(SyncGrant grant)
    {
        if (!Guid.TryParseExact(grant.Id, "N", out _) || !Guid.TryParseExact(grant.SpaceId, "N", out _) || grant.PeerKey.Length != 64 || !grant.PeerKey.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid sync permission.");
        Mutate(() => { data.Grants.RemoveAll(item => item.Id == grant.Id && item.PeerKey == grant.PeerKey); if (data.Grants.Count >= 64) throw new IOException("Remove an old sync permission first."); data.Grants.Add(grant); });
    }
    public void SaveLink(SyncLink link)
    {
        FileCatalog.CheckSafeDirectory(link.Folder);
        if (!Directory.Exists(link.Folder) || !Guid.TryParseExact(link.Id, "N", out _) || !Guid.TryParseExact(link.SpaceId, "N", out _) || link.PeerKey.Length != 64 || !link.PeerKey.All(char.IsAsciiHexDigit) || link.Baseline.Count > FileCatalog.MaxEntries)
            throw new InvalidDataException("Invalid sync folder settings.");
        foreach ((string path, string hash) in link.Baseline)
        {
            FileCatalog.ValidateRelative(path);
            if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Invalid saved sync checksum.");
        }
        Mutate(() =>
        {
            if (data.Links.Any(item => item.Id != link.Id && Overlap(item.Folder, link.Folder)))
                throw new IOException("Choose a separate folder. Sync destinations cannot overlap.");
            data.Links.RemoveAll(item => item.Id == link.Id);
            if (data.Links.Count >= 16)
                throw new IOException("Up to 16 sync folders are supported.");
            data.Links.Add(link with
            {
                Folder = Path.GetFullPath(link.Folder),
                Baseline = new(link.Baseline, StringComparer.OrdinalIgnoreCase)
            });
        });
    }
    public static bool Overlap(string first, string second)
    {
        string a = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string b = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }
    public void RemoveLink(string id) => Mutate(() => data.Links.RemoveAll(link => link.Id == id));
    public void SetEnabled(string id, bool enabled) => Mutate(() =>
    {
        int at = data.Links.FindIndex(link => link.Id == id);
        if (at < 0)
            throw new IOException("This sync link was removed.");
        data.Links[at] = data.Links[at] with
        {
            Enabled = enabled
        };
    });
    public void Revoke(string id, string key) => Mutate(() => data.Grants.RemoveAll(grant => grant.Id == id && grant.PeerKey == key));
    private void Mutate(Action mutation)
    {
        lock (gate)
        {
            Document previous = data with
            {
                Links = data.Links.ToList(),
                Grants = data.Grants.ToList()
            };
            try
            {
                mutation();
                FileCatalog.CheckSafeDirectory(Path.GetDirectoryName(filename)!);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, MetadataStore.Json);
                if (bytes.Length > 24 * 1024 * 1024)
                    throw new IOException("Sync settings reached their safety limit. Remove an unused link.");
                new MetadataStore.AtomicDiskWriter().Write(filename, bytes);
            }
            catch { data = previous; throw; }
        }
        Changed?.Invoke();
    }
}
