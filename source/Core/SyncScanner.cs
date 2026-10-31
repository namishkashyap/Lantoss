using System.Collections.Concurrent;

namespace Lantoss.Core;

public sealed record ScanStatistics(long MetadataScans, long HashedFiles, long HashedBytes, long CacheHits, int WatchedRoots, int CachedFiles);

/// <summary>Watchers invalidate hashes, metadata sweeps recover missed events, and idle requests reuse an immutable snapshot.</summary>
public sealed class SyncScanner(Func<DateTime>? utcNow = null) : IDisposable
{
    private DateTime Now => utcNow?.Invoke() ?? DateTime.UtcNow;
    private sealed record Cached(long Length, DateTime Modified, DateTime Checked, string Hash);
    private sealed class RootState(string folder) : IDisposable
    {
        public readonly string Folder = folder;
        public readonly SemaphoreSlim Slot = new(1, 1);
        public readonly ConcurrentDictionary<string, long> Dirty = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Cached> Files = new(StringComparer.OrdinalIgnoreCase);
        public FileSystemWatcher? Watcher;
        public Manifest? Snapshot;
        public DateTime LastScan, Used = DateTime.UtcNow, WatchRetry;
        public long Version, EventSequence, ScannedVersion = -1;
        public int Pins, AllDirty, RestartWatcher;
        public void Dispose() => Watcher?.Dispose();
    }
    private readonly object gate = new();
    private readonly Dictionary<string, RootState> roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim hashSlot = new(1, 1);
    private long scans, hashes, hashedBytes, hits;
    private bool disposed;
    public ScanStatistics Statistics
    {
        get
        {
            lock (gate)
                return new(Interlocked.Read(ref scans), Interlocked.Read(ref hashes), Interlocked.Read(ref hashedBytes), Interlocked.Read(ref hits), roots.Count, roots.Values.Sum(state => state.Files.Count));
        }
    }
    private RootState Acquire(string folder)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!roots.TryGetValue(folder, out RootState? state))
            {
                state = new(folder);
                roots.Add(folder, state);
            }
            if ((state.Watcher == null || Volatile.Read(ref state.RestartWatcher) != 0) && Now >= state.WatchRetry)
            {
                state.Watcher?.Dispose();
                state.Watcher = null;
                state.WatchRetry = Now.AddSeconds(30);
                Interlocked.Exchange(ref state.RestartWatcher, 0);
                FileSystemWatcher? watcher = null;
                try
                {
                    watcher = new(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.Security,
                        InternalBufferSize = 16384
                    };
                    void Mark(string path)
                    {
                        if (path.Contains(Path.DirectorySeparatorChar + ".flowshare-", StringComparison.OrdinalIgnoreCase))
                            return;
                        if (state.Dirty.Count >= 20000)
                        {
                            Interlocked.Exchange(ref state.AllDirty, 1);
                            state.Dirty.Clear();
                        }
                        state.Dirty[path] = Interlocked.Increment(ref state.EventSequence);
                        Interlocked.Increment(ref state.Version);
                    }
                    watcher.Changed += (_, e) => Mark(e.FullPath);
                    watcher.Created += (_, e) => Mark(e.FullPath);
                    watcher.Deleted += (_, e) => Mark(e.FullPath);
                    watcher.Renamed += (_, e) => { Mark(e.OldFullPath); Mark(e.FullPath); };
                    watcher.Error += (_, _) => { Interlocked.Exchange(ref state.AllDirty, 1); Interlocked.Exchange(ref state.RestartWatcher, 1); Interlocked.Increment(ref state.Version); };
                    watcher.EnableRaisingEvents = true;
                    state.Watcher = watcher;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { watcher?.Dispose(); Interlocked.Exchange(ref state.RestartWatcher, 1); /* Poll metadata if notifications are unavailable. */ }
            }
            state.Pins++;
            state.Used = Now;
            return state;
        }
    }
    private void Release(RootState state)
    {
        lock (gate)
        {
            state.Pins--;
            // Evict entire cold roots rather than clearing every cached hash in one burst.
            foreach (RootState cold in roots.Values.Where(item => item.Pins == 0).OrderBy(item => item.Used).ToArray())
            {
                if (roots.Count <= 32 && roots.Values.Sum(item => item.Files.Count) <= 20000)
                    break;
                roots.Remove(cold.Folder);
                cold.Dispose();
            }
        }
    }
    public async Task<Manifest> ScanAsync(string root, CancellationToken token, bool force = false)
    {
        token.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        FileCatalog.CheckSafeDirectory(root);
        if (!Directory.Exists(root))
            throw new IOException("The sync folder is unavailable. Restore its drive/folder; it will not be recreated automatically.");
        RootState state = Acquire(root);
        bool locked = false;
        try
        {
            await state.Slot.WaitAsync(token);
            locked = true;
            long version = Interlocked.Read(ref state.Version);
            if (!force && state.Watcher != null && Volatile.Read(ref state.RestartWatcher) == 0 && Volatile.Read(ref state.AllDirty) == 0 && state.Dirty.IsEmpty && state.Snapshot != null && version == state.ScannedVersion && Now - state.LastScan < TimeSpan.FromSeconds(60))
            {
                Interlocked.Increment(ref hits);
                return state.Snapshot;
            }
            Interlocked.Increment(ref scans);
            bool allDirty = Interlocked.Exchange(ref state.AllDirty, 0) != 0;
            KeyValuePair<string, long>[] changes = state.Dirty.ToArray();
            HashSet<string> dirty = new(changes.Select(change => change.Key), StringComparer.OrdinalIgnoreCase);
            // Remove only the captured generation: a newer edit to the same path must survive this pass.
            foreach (KeyValuePair<string, long> change in changes)
                ((ICollection<KeyValuePair<string, long>>)state.Dirty).Remove(change);
            if (allDirty)
                state.Files = new(StringComparer.OrdinalIgnoreCase);
            foreach (string changed in dirty)
            {
                if (state.Files.Remove(changed) || File.Exists(changed))
                    continue;
                string prefix = changed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string old in state.Files.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                    state.Files.Remove(old);
            }
            List<ManifestEntry> entries = [];
            Dictionary<string, Cached> next = new(StringComparer.OrdinalIgnoreCase);
            int auditBudget = 1;
            await Walk(root, "", 0);
            Manifest? previous = state.Snapshot;
            bool identical = previous != null && previous.Entries.Count == entries.Count && previous.Entries.Zip(entries).All(pair => pair.First == pair.Second);
            Manifest snapshot = identical ? previous! : new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Entries = entries
            };
            state.Files = next;
            state.Snapshot = snapshot;
            state.LastScan = Now;
            // An event arriving during enumeration forces another pass, instead of being marked handled.
            state.ScannedVersion = version;
            return snapshot;

            async Task Walk(string folder, string relative, int depth)
            {
                if (depth > 128)
                    throw new IOException("This sync folder has too many nested folders.");
                foreach (string path in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(path);
                    // Shell-control files and names with hidden characters are never offered to peers.
                    if (name.StartsWith(".flowshare-", StringComparison.OrdinalIgnoreCase) || name.Any(FileCatalog.Hidden) || (FileCatalog.ShellControl(name) && File.Exists(path)))
                        continue;
                    FileCatalog.ValidateSegment(name);
                    FileCatalog.CheckSafePath(path);
                    if (entries.Count >= FileCatalog.MaxEntries)
                        throw new IOException("A sync folder supports up to 10,000 files and folders.");
                    string child = relative.Length == 0 ? name : relative + "/" + name;
                    if (Directory.Exists(path))
                    {
                        entries.Add(new()
                        {
                            RelativePath = child,
                            Directory = true
                        });
                        await Walk(path, child, depth + 1);
                        continue;
                    }
                    FileInfo info = new(path);
                    long size = info.Length;
                    DateTime modified = info.LastWriteTimeUtc;
                    if (size > FileCatalog.MaxFileBytes)
                        throw new IOException("A sync file exceeds the 1 TiB safety ceiling.");
                    state.Files.TryGetValue(path, out Cached? cached);
                    bool audit = cached != null && Now - cached.Checked > TimeSpan.FromMinutes(30) && auditBudget > 0;
                    if (force || cached == null || cached.Length != size || cached.Modified != modified || audit)
                    {
                        if (audit)
                            auditBudget--;
                        // One checksum reader across all roots keeps background I/O and hash CPU bounded.
                        await hashSlot.WaitAsync(token);
                        string hash;
                        try
                        {
                            hash = await SyncFiles.HashAsync(path, token);
                            Interlocked.Increment(ref hashes);
                            Interlocked.Add(ref hashedBytes, size);
                        }
                        finally { hashSlot.Release(); }
                        info.Refresh();
                        if (!info.Exists || info.Length != size || info.LastWriteTimeUtc != modified)
                            throw new IOException("A file is still being edited. Sync will retry.");
                        cached = new(size, modified, Now, hash);
                    }
                    next[path] = cached!;
                    // Retain validated progress if a later file is locked; never publish a partial manifest.
                    state.Files[path] = cached!;
                    entries.Add(new()
                    {
                        RelativePath = child,
                        Size = size,
                        Sha256 = cached!.Hash,
                        SourcePath = path,
                        SourceModifiedUtc = modified
                    });
                }
            }
        }
        catch
        {
            state.ScannedVersion = -1;
            throw;
        }
        finally { if (locked) state.Slot.Release(); Release(state); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            foreach (RootState state in roots.Values)
                state.Dispose();
            roots.Clear();
        }
    }
}
