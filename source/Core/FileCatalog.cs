using System.Security.Cryptography;

namespace Lantoss.Core;

/// <summary>
/// Validates local selections and remote relative paths before any file operation. Reject traversal, reserved names and reparse points instead of following them.
/// </summary>
public static class FileCatalog
{
    public const int MaxEntries = 10_000;
    public const long MaxFileBytes = 1024L * 1024 * 1024 * 1024;
    public static async Task<Manifest> BuildAsync(IEnumerable<string> roots, string id, bool hash, CancellationToken cancellation, Action<string>? preparing = null)
    {
        List<ManifestEntry> entries = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(raw);
            if (File.Exists(full))
                Add(full, Path.GetFileName(full), false);
            else if (Directory.Exists(full))
            {
                string rootName = new DirectoryInfo(full).Name;
                Walk(full, rootName);
            }
            else
                throw new FileNotFoundException("A selected file or folder is no longer available: " + full);
        }
        if (entries.Count == 0)
            throw new InvalidDataException("Choose at least one file or folder.");
        if (hash)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                if (entry.Directory)
                    continue;
                cancellation.ThrowIfCancellationRequested();
                preparing?.Invoke(entry.RelativePath);
                await using FileStream stream = new(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation));
                FileInfo after = new(entry.SourcePath);
                if (after.Length != entry.Size || after.LastWriteTimeUtc != entry.SourceModifiedUtc)
                    throw new IOException("A source file changed while preparing: " + entry.RelativePath);
                entries[i] = entry with
                {
                    Sha256 = digest
                };
            }
        }
        return new Manifest { Id = id, Entries = entries };

        void Walk(string directory, string relative)
        {
            cancellation.ThrowIfCancellationRequested();
            Add(directory, relative, true);
            foreach (string child in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellation.ThrowIfCancellationRequested();
                if (Path.GetFileName(child).StartsWith(".flowshare-", StringComparison.OrdinalIgnoreCase))
                    continue;
                FileAttributes attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symbolic links, junctions, and cloud placeholders are not supported: " + child);
                string childRelative = relative + "/" + Path.GetFileName(child);
                if ((attributes & FileAttributes.Directory) != 0)
                    Walk(child, childRelative);
                else
                    Add(child, childRelative, false);
            }
        }
        void Add(string source, string relative, bool directory)
        {
            if (entries.Count >= MaxEntries)
                throw new InvalidDataException($"Select up to {MaxEntries:N0} files and folders per transfer.");
            ValidateRelative(relative);
            CheckSafePath(source);
            if (!seen.Add(relative))
                throw new InvalidDataException("Two selections have the same relative name. Rename one before sending: " + relative);
            FileInfo info = new(source);
            long size = directory ? 0 : info.Length;
            if (size > MaxFileBytes)
                throw new InvalidDataException("A selected file exceeds the 1 TB safety limit.");
            entries.Add(new ManifestEntry { RelativePath = relative, Directory = directory, Size = size, SourcePath = source, SourceModifiedUtc = directory ? DateTime.MinValue : info.LastWriteTimeUtc });
        }
    }
    public static void ValidateManifest(Manifest manifest)
    {
        if (!Guid.TryParseExact(manifest.Id, "N", out _) || manifest.Entries is null || manifest.Entries.Count is < 1 or > MaxEntries)
            throw new InvalidDataException("The transfer manifest is invalid or too large.");
        Dictionary<string, bool> seen = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ManifestEntry entry in manifest.Entries)
        {
            if (entry is null || entry.RelativePath is null || entry.Sha256 is null)
                throw new InvalidDataException("An incoming manifest entry is invalid.");
            ValidateRelative(entry.RelativePath);
            if (!seen.TryAdd(entry.RelativePath, entry.Directory))
                throw new InvalidDataException("The transfer contains conflicting paths.");
            if (entry.Size < 0 || entry.Size > MaxFileBytes || (entry.Directory && entry.Size != 0))
                throw new InvalidDataException("An incoming file size is invalid.");
            if (!entry.Directory && (entry.Sha256.Length != 64 || !entry.Sha256.All(c => char.IsAsciiHexDigit(c))))
                throw new InvalidDataException("A file is missing a valid SHA-256 checksum.");
            total = checked(total + entry.Size);
            if (total > 2 * MaxFileBytes)
                throw new InvalidDataException("This transfer exceeds the 2 TB safety limit.");
        }
        foreach (string path in seen.Keys)
        {
            string parent = path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (!seen.TryGetValue(parent, out bool isDirectory) || !isDirectory)
                    throw new InvalidDataException("A transfer parent folder is missing or conflicts with a file.");
            }
        }
    }
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Contains('\\') || path.StartsWith('/') || path.EndsWith('/'))
            throw new InvalidDataException("An incoming path is invalid.");
        foreach (string segment in path.Split('/'))
            ValidateSegment(segment);
    }
    public static void ValidateSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name != name.Trim() || name.EndsWith('.') || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl) || name.StartsWith(".flowshare-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported Windows filename: " + name);
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$"];
        if (reserved.Contains(stem) || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9¹²³]$"))
            throw new InvalidDataException("Reserved Windows filename: " + name);
    }
    public static void CheckSafeDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith("\\\\", StringComparison.Ordinal))
            throw new IOException("Use a local drive. Mounted shares and NAS folders are planned for a later release.");
        if (File.Exists(full))
            throw new IOException("Choose a folder, rather than an existing file.");
        CheckSafePath(full);
    }
    public static void CheckSafePath(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links, junctions, and cloud placeholders are not supported: " + current);
            current = Path.GetDirectoryName(current);
        }
    }
    public static long FreeSpace(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
    public static string Within(string root, string relative)
    {
        ValidateRelative(relative);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || result.Length > 32000)
            throw new InvalidDataException("A received path would leave the chosen folder.");
        return result;
    }
    public static string Unique(string path, ISet<string>? reserved = null)
    {
        string parent = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path);
        for (int i = 0; i < 10000; i++)
        {
            string candidate = i == 0 ? path : Path.Combine(parent, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate) && !(reserved?.Contains(candidate) ?? false))
                return candidate;
        }
        throw new IOException("Too many files already use the same name.");
    }
}

public sealed record ReceiveItem(int Index, ManifestEntry Entry, string Destination, bool Skip, bool Replace);
public sealed class ReceivePlan
{
    public string Root
    {
        get;
    }
    public ReceiveItem[] Items
    {
        get;
    }
    public long Bytes => Items.Where(i => !i.Skip).Sum(i => i.Entry.Size);
    public ReceivePlan(Manifest manifest, string root, DuplicatePolicy duplicates)
    {
        FileCatalog.ValidateManifest(manifest);
        FileCatalog.CheckSafeDirectory(root);
        Root = Path.GetFullPath(root);
        Dictionary<string, string> mapped = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reserved = new(StringComparer.OrdinalIgnoreCase);
        List<ReceiveItem> result = [];
        foreach ((ManifestEntry entry, int index) in manifest.Entries.Select((e, i) => (e, i)).OrderBy(t => t.e.RelativePath.Count(c => c == '/')).ThenByDescending(t => t.e.Directory))
        {
            int slash = entry.RelativePath.LastIndexOf('/');
            string path = slash < 0 ? FileCatalog.Within(Root, entry.RelativePath) : Path.Combine(mapped[entry.RelativePath[..slash]], entry.RelativePath[(slash + 1)..]);
            FileCatalog.CheckSafePath(path);
            bool exists = File.Exists(path) || Directory.Exists(path);
            bool skip = !entry.Directory && exists && duplicates == DuplicatePolicy.Skip;
            if (exists && duplicates == DuplicatePolicy.KeepBoth)
                path = FileCatalog.Unique(path, reserved);
            else if (entry.Directory && File.Exists(path))
                throw new IOException("An incoming folder conflicts with an existing file. Choose Keep both.");
            else if (!entry.Directory && Directory.Exists(path) && !skip)
                throw new IOException("An incoming file conflicts with an existing folder. Choose Keep both or Skip.");
            reserved.Add(path);
            mapped[entry.RelativePath] = path;
            result.Add(new(index, entry, path, skip, duplicates == DuplicatePolicy.Replace));
        }
        Items = result.OrderBy(i => i.Index).ToArray();
    }
    public void CreateDirectories()
    {
        FileCatalog.CheckSafeDirectory(Root);
        Directory.CreateDirectory(Root);
        foreach (ReceiveItem item in Items.Where(i => i.Entry.Directory))
        {
            FileCatalog.CheckSafeDirectory(item.Destination);
            Directory.CreateDirectory(item.Destination);
        }
    }
    public string FinalizeFile(string temporary, ReceiveItem item)
    {
        FileCatalog.CheckSafePath(item.Destination);
        string target = item.Destination;
        if (item.Replace && File.Exists(target))
            File.Replace(temporary, target, null);
        else
        {
            while (true)
            {
                target = FileCatalog.Unique(target);
                try
                {
                    File.Move(temporary, target);
                    break;
                }
                catch (IOException) { if (!File.Exists(target) && !Directory.Exists(target)) throw; }
            }
        }
        return target;
    }
}
