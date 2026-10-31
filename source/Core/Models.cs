using System.Text.Json.Serialization;

namespace Lantoss.Core;

public sealed record Preferences
{
    public string DeviceName { get; init; } = Environment.MachineName;
    public string ReceiveFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Lantoss Inbox");
    public string Theme { get; init; } = "System";
    public bool Notifications { get; init; } = true;
    public bool Discoverable { get; init; } = true;
    public bool StartReceivingOnOpen { get; init; } = true;
    public bool RequireConnectionCode
    {
        get; init;
    }
    public bool BrowserSharingEnabled { get; init; } = true;
    public bool AutoConnect { get; init; } = true;
    public bool AutoConfigureFirewall { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public bool SenderFolders
    {
        get; init;
    }
    /// <summary>The phone page name, published as http://LocalName.local.</summary>
    public string LocalName { get; init; } = "share-wollvo";
    /// <summary>A new computer or phone waits for approval on this PC; already paired PCs reconnect without asking.</summary>
    public bool ApproveNewDevices { get; init; } = true;
    /// <summary>The phone page is served over HTTPS with this PC's own certificate. Off by default: phones warn about a self-signed certificate.</summary>
    /// <remarks>Saved under a new name so installs that had the old default (on) switch to HTTP once.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("BrowserHttpsOn")]
    public bool BrowserHttps
    {
        get; init;
    }
}

public sealed record Peer
{
    public required string Fingerprint
    {
        get; init;
    }
    public required string Name
    {
        get; init;
    }
    public required string Address
    {
        get; init;
    }
    public int Port
    {
        get; init;
    }
    public string BrowserUrl { get; init; } = "";
    [JsonIgnore]
    public bool BrowserAdvertised
    {
        get; init;
    }
    public bool Paired
    {
        get; init;
    }
    public bool SupportsQuickPair
    {
        get; init;
    }
    public bool SupportsDirectConnect
    {
        get; init;
    }
    public bool RequiresCode
    {
        get; init;
    }
    public bool KeyVerified { get; init; } = true;
    public bool Blocked
    {
        get; init;
    }
    public bool Favorite
    {
        get; init;
    }
    public DateTime LastSeenUtc { get; init; } = DateTime.UtcNow;
    [JsonIgnore] public string DisplayName => Name;
    [JsonIgnore] public string Endpoint => $"{Address}:{Port}";
    [JsonIgnore] public bool Online => DateTime.UtcNow - LastSeenUtc < TimeSpan.FromSeconds(75);
    [JsonIgnore] public string TrustLabel => Blocked ? "Blocked" : Paired ? KeyVerified ? "Paired · key verified" : "Connected · saved key" : "Nearby · not verified";
    public override string ToString() => Name;
}

public enum SpaceAccess
{
    ReadOnly, ReadAndUpload
}
/// <summary>AllowedPeers empty means every paired PC; otherwise only the listed device keys can open the space.</summary>
public sealed record SharedSpace(string Id, string Name, string Folder, SpaceAccess Access = SpaceAccess.ReadOnly, bool Enabled = true, string[]? AllowedPeers = null)
{
    public bool Allows(string fingerprint) => AllowedPeers is not { Length: > 0 } || AllowedPeers.Contains(fingerprint, StringComparer.Ordinal);
}
public sealed record SpaceEntry(string Name, string Path, bool Directory, long Size);
public sealed record RemoteSpace(string Id, string Name, bool CanUpload, bool Available);
public sealed record SyncGrant(string Id, string PeerKey, string SpaceId);
public sealed record SyncLink(string Id, string PeerKey, string SpaceId, string Name, string Folder, bool Enabled = true)
{
    public Dictionary<string, string> Baseline { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastSuccessUtc
    {
        get; init;
    }
}
public sealed record SyncApproval(string PeerName, string PeerKey, SharedSpace Space);
public sealed record SyncStatus(string Id, string Message, bool Busy = false, DateTime? LastSuccessUtc = null);

public enum TransferState
{
    Preparing, Queued, AwaitingApproval, Sending, Receiving, Verifying, Completed, Declined, Cancelled, Failed
}
public enum DuplicatePolicy
{
    KeepBoth, Skip, Replace
}

public sealed record TransferFile
{
    public required string RelativePath
    {
        get; init;
    }
    public long Size
    {
        get; init;
    }
    public long Bytes
    {
        get; init;
    }
    public string Status { get; init; } = "Queued";
}

public sealed record TransferRecord
{
    public required string Id
    {
        get; init;
    }
    public required string Direction
    {
        get; init;
    }
    public required string PeerName
    {
        get; init;
    }
    public required string PeerFingerprint
    {
        get; init;
    }
    public string PeerAddress { get; init; } = "";
    public string Transport { get; init; } = "TLS 1.2";
    public string SpaceId { get; init; } = "";
    public int PeerPort
    {
        get; init;
    }
    public string Title { get; init; } = "Files";
    public TransferState State
    {
        get; init;
    }
    public long TotalBytes
    {
        get; init;
    }
    public long TransferredBytes
    {
        get; init;
    }
    public double BytesPerSecond
    {
        get; init;
    }
    public int FileCount
    {
        get; init;
    }
    public int FolderCount
    {
        get; init;
    }
    public int VerifiedFiles
    {
        get; init;
    }
    public int SkippedFiles
    {
        get; init;
    }
    public string CurrentFile { get; init; } = "";
    public string Error { get; init; } = "";
    public string Destination { get; init; } = "";
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
    public List<string> Sources { get; init; } = [];
    public List<TransferFile> Files { get; init; } = [];
    [JsonIgnore] public bool Terminal => State is TransferState.Completed or TransferState.Declined or TransferState.Cancelled or TransferState.Failed;
    [JsonIgnore] public double Progress => TotalBytes > 0 ? Math.Clamp(100.0 * TransferredBytes / TotalBytes, 0, 100) : State == TransferState.Completed ? 100 : 0;
    [JsonIgnore] public string SizeText => Format.Bytes(TotalBytes);
    [JsonIgnore] public string ProgressText => $"{Format.Bytes(TransferredBytes)} of {Format.Bytes(TotalBytes)}";
    [JsonIgnore] public string SpeedText => BytesPerSecond > 0 && !Terminal ? $"{BytesPerSecond / 1048576:0.0} MB/s" : "—";
    [JsonIgnore] public string TimeLeft => BytesPerSecond > 0 && !Terminal ? Format.Duration((TotalBytes - TransferredBytes) / BytesPerSecond) + " left" : "";
    [JsonIgnore] public string StatusText => State == TransferState.Completed && SkippedFiles > 0 ? $"Completed · {SkippedFiles} skipped" : State == TransferState.AwaitingApproval ? "Awaiting approval" : State.ToString();
}

public sealed record ManifestEntry
{
    public required string RelativePath
    {
        get; init;
    }
    public bool Directory
    {
        get; init;
    }
    public long Size
    {
        get; init;
    }
    public string Sha256 { get; init; } = "";
    [JsonIgnore] public string SourcePath { get; init; } = "";
    [JsonIgnore]
    public DateTime SourceModifiedUtc
    {
        get; init;
    }
}
public sealed record Manifest
{
    public required string Id
    {
        get; init;
    }
    public List<ManifestEntry> Entries { get; init; } = [];
    [JsonIgnore] public long TotalBytes => Entries.Sum(e => e.Size);
    [JsonIgnore] public int FileCount => Entries.Count(e => !e.Directory);
    [JsonIgnore] public int FolderCount => Entries.Count(e => e.Directory);
}
public sealed record SelectionItem(string Path, string Name, long Size, int Files, int Folders)
{
    public string Detail => $"{Format.Bytes(Size)} · {Files} {(Files == 1 ? "file" : "files")}" + (Folders > 0 ? $" · {Folders} folders" : "");
}
public sealed record PairRequest(string Name, string Fingerprint, string ComparisonCode, bool Incoming);
public sealed record ReceiveRequest(string PeerName, string Fingerprint, Manifest Manifest, string DefaultFolder, long FreeBytes, bool KeyVerified = true, bool Browser = false, bool FixedFolder = false, string Address = "", byte[]? Picture = null);
public sealed record ReceiveDecision(bool Accepted, string Folder, DuplicatePolicy Duplicates = DuplicatePolicy.KeepBoth);

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
    public static string Duration(double seconds) => seconds < 60 ? $"{Math.Max(1, (int)seconds)}s" : seconds < 3600 ? $"{(int)(seconds / 60)}m {((int)seconds % 60)}s" : $"{seconds / 3600:0.0}h";
    public static string ShortKey(string key) => key.Length >= 16 ? $"{key[..4]} {key[4..8]} {key[8..12]} {key[12..16]}" : key;
}
