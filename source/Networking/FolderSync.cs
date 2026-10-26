using System.Collections.Concurrent;
using Lantoss.Core;

namespace Lantoss.Networking;

/// <summary>
/// A single initiating coordinator reconciles each linked folder pair. Polling plus periodic rehashing avoids depending on potentially missed filesystem watcher events.
/// </summary>
public sealed partial class TransferEngine
{
    private readonly ConcurrentDictionary<string, SyncStatus> syncStatuses = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> syncLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeSync = new();
    private sealed record RemoteSnapshot(string Revision, Manifest Manifest);
    private readonly ConcurrentDictionary<string, RemoteSnapshot> remoteSnapshots = new();
    private readonly ConcurrentDictionary<string, (string Local, string Remote)> reconciled = new();
    public ScanStatistics SyncScanStatistics => spaceScanner.Statistics;
    public event Action? SyncChanged;
    public SyncStatus[] SyncStatuses => Sync.Headers.Select(link => syncStatuses.GetValueOrDefault(link.Id) ?? new(link.Id, link.Enabled ? "Waiting for the other PC" : "Paused", false, link.LastSuccessUtc)).ToArray();
    public async Task<SyncLink> LinkSpaceAsync(Peer peer, RemoteSpace space, string folder, CancellationToken token = default)
    {
        FileCatalog.CheckSafeDirectory(folder);
        if (!Directory.Exists(folder))
            throw new IOException("Choose an existing local folder.");
        if (!space.CanUpload)
            throw new IOException("On the host PC, change this space to Read & upload before enabling two-way sync.");
        if (Sync.Headers.Any(link => SyncStore.Overlap(link.Folder, folder)))
            throw new IOException("This folder overlaps an existing sync link. Choose a separate folder.");
        SyncLink link = new(Guid.NewGuid().ToString("N"), peer.Fingerprint, space.Id, space.Name + " · " + peer.Name, Path.GetFullPath(folder));
        WireMessage granted = await SpaceRequestAsync(peer, new()
        {
            Kind = "sync-grant",
            SpaceId = space.Id,
            SyncId = link.Id
        }, token);
        Wire.Require(granted, "sync-granted");
        Sync.SaveLink(link);
        SetSyncStatus(link, "Linked. Automatic sync will start shortly.");
        return link;
    }
    public void PauseSync(string id, bool paused)
    {
        SyncLink link = Sync.GetLink(id) ?? throw new IOException("This sync link was removed.");
        if (activeSync.TryGetValue(id, out CancellationTokenSource? work))
            work.Cancel();
        Sync.SetEnabled(id, !paused);
        SetSyncStatus(link, paused ? "Paused. Local files are kept." : "Resuming automatic sync.");
    }
    public void RemoveSync(string id)
    {
        if (activeSync.TryGetValue(id, out CancellationTokenSource? work))
            work.Cancel();
        Sync.RemoveLink(id);
        remoteSnapshots.TryRemove(id, out _);
        reconciled.TryRemove(id, out _);
        syncStatuses.TryRemove(id, out _);
        SyncChanged?.Invoke();
    }
    private void SetSyncStatus(SyncLink link, string message, bool busy = false)
    {
        SyncStatus next = new(link.Id, message, busy, Sync.LastSuccess(link.Id));
        if (syncStatuses.TryGetValue(link.Id, out SyncStatus? previous) && previous == next)
            return;
        syncStatuses[link.Id] = next;
        SyncChanged?.Invoke();
    }
    private async Task SyncLoopAsync(CancellationToken token)
    {
        Dictionary<string, (int Failures, DateTime Retry)> retry = [];
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (SyncLink link in Sync.Headers.Where(item => item.Enabled))
                {
                    if (retry.TryGetValue(link.Id, out var delay) && delay.Retry > DateTime.UtcNow)
                        continue;
                    try
                    {
                        await SyncOnceAsync(link.Id, token, false);
                        retry.Remove(link.Id);
                    }
                    catch (Exception error) when (!token.IsCancellationRequested)
                    {
                        int failures = Math.Min(5, retry.GetValueOrDefault(link.Id).Failures + 1);
                        int seconds = Math.Min(60, 2 << failures);
                        retry[link.Id] = (failures, DateTime.UtcNow.AddSeconds(seconds));
                        SetSyncStatus(link, HumanError(error) + $" Retrying in {seconds}s.");
                        Diagnostic?.Invoke(error);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task SyncOnceAsync(string id, CancellationToken token = default, bool force = true)
    {
        if (!Running)
            throw new IOException("Start sharing before syncing folders.");
        SemaphoreSlim mutex = syncLocks.GetOrAdd(id, _ => new(1, 1));
        await mutex.WaitAsync(token);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime?.Token ?? CancellationToken.None);
        try
        {
            SyncLink link = Sync.GetHeader(id) ?? throw new OperationCanceledException("This sync link is paused or removed.");
            if (!link.Enabled)
                throw new OperationCanceledException("This sync link is paused.");
            activeSync[id] = work;
            Peer peer = store.Trusted(link.PeerKey) ?? throw new IOException("Reconnect this PC to restore its saved sync permission.");
            void LocalGuard()
            {
                work.Token.ThrowIfCancellationRequested();
                if (!Sync.IsEnabled(id, link.Folder))
                    throw new OperationCanceledException();
                FileCatalog.CheckSafeDirectory(link.Folder);
                if (!Directory.Exists(link.Folder))
                    throw new IOException("The local sync folder is unavailable.");
            }
            LocalGuard();
            if (force || !reconciled.ContainsKey(id))
                SetSyncStatus(link, "Checking file changes…", true);
            Manifest local = await spaceScanner.ScanAsync(link.Folder, work.Token, force);
            remoteSnapshots.TryGetValue(id, out RemoteSnapshot? savedRemote);
            WireMessage response = await SpaceRequestAsync(peer, new()
            {
                Kind = "space-snapshot",
                KnownRevision = force ? "" : savedRemote?.Revision ?? "",
                SpaceId = link.SpaceId,
                SyncId = link.Id
            }, work.Token);
            Manifest remote;
            if (response.Kind == "snapshot-unchanged" && !force && savedRemote != null && response.SnapshotRevision == savedRemote.Revision)
                remote = savedRemote.Manifest;
            else
            {
                Wire.Require(response, "snapshot");
                remote = response.Manifest ?? throw new ProtocolException("Missing shared folder snapshot.");
                if (response.SnapshotRevision.Length == 32)
                    remoteSnapshots[id] = new(response.SnapshotRevision, remote);
                else
                    remoteSnapshots.TryRemove(id, out _); // Older PCs still send a full, compatible snapshot.
            }
            if (!force && response.SnapshotRevision.Length == 32 && reconciled.TryGetValue(id, out var agreed) && agreed.Local == local.Id && agreed.Remote == response.SnapshotRevision)
            {
                LocalGuard();
                SetSyncStatus(link, "Up to date · watching for changes.");
                return;
            }
            SyncFiles.ValidateSnapshot(remote);
            LocalGuard();
            link = Sync.GetLink(id) ?? throw new OperationCanceledException("This sync link was removed.");
            SetSyncStatus(link, "Checking file changes…", true);
            Dictionary<string, ManifestEntry> left = local.Entries.ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ManifestEntry> right = remote.Entries.ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
            // The last agreed content hash distinguishes a one-sided edit from simultaneous edits.
            // Missing paths are restored from the surviving side; deletions intentionally do not propagate.
            Dictionary<string, string> baseline = new(link.Baseline, StringComparer.OrdinalIgnoreCase);
            foreach (string old in baseline.Keys.Where(path => !left.ContainsKey(path) && !right.ContainsKey(path)).ToArray())
                baseline.Remove(old);
            int changed = 0, conflicts = 0;
            foreach (string path in left.Keys.Concat(right.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Count(c => c == '/')).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                LocalGuard();
                left.TryGetValue(path, out ManifestEntry? l);
                right.TryGetValue(path, out ManifestEntry? r);
                if (l?.Directory == true || r?.Directory == true)
                {
                    if (l != null && r != null && l.Directory != r.Directory)
                        throw new IOException("A file and folder have the same name: " + path + ". Rename one to continue.");
                    if (l == null)
                        SyncFiles.MakeDirectory(link.Folder, path);
                    if (r == null)
                    {
                        WireMessage made = await SpaceRequestAsync(peer, new()
                        {
                            Kind = "sync-mkdir",
                            SpaceId = link.SpaceId,
                            SyncId = link.Id,
                            RelativePath = path
                        }, work.Token);
                        Wire.Require(made, "complete");
                    }
                    continue;
                }
                if (l?.Sha256 == r?.Sha256)
                {
                    if (l != null)
                        baseline[path] = l.Sha256;
                    continue;
                }
                baseline.TryGetValue(path, out string? previous);
                if (l == null || (previous != null && l.Sha256 == previous))
                {
                    await FetchSpaceFileAsync(peer, link.SpaceId, path, link.Folder, path, l?.Sha256 ?? "", r!.Sha256, work.Token, link.Id);
                    baseline[path] = r.Sha256;
                    changed++;
                    continue;
                }
                if (r == null || (previous != null && r.Sha256 == previous))
                {
                    await PushSyncFileAsync(peer, link, l, path, r?.Sha256 ?? "", work.Token);
                    baseline[path] = l.Sha256;
                    changed++;
                    continue;
                }
                // Initial same-name differences and simultaneous edits preserve both bytes.
                string conflict = SyncFiles.ConflictPath(path, r.Sha256);
                string conflictLocal = SyncFiles.PathIn(link.Folder, conflict);
                string existingConflict = await SyncFiles.HashAsync(conflictLocal, work.Token);
                if (existingConflict.Length > 0 && existingConflict != r.Sha256)
                    throw new IOException("A conflict-copy name is already in use. Rename it to continue: " + conflict);
                if (existingConflict.Length == 0)
                    await FetchSpaceFileAsync(peer, link.SpaceId, path, link.Folder, conflict, "", r.Sha256, work.Token, link.Id);
                ManifestEntry copy = r with
                {
                    RelativePath = conflict,
                    SourcePath = conflictLocal,
                    SourceModifiedUtc = File.GetLastWriteTimeUtc(conflictLocal)
                };
                string existingRemoteConflict = right.GetValueOrDefault(conflict)?.Sha256 ?? "";
                if (existingRemoteConflict.Length > 0 && existingRemoteConflict != r.Sha256)
                    throw new IOException("A remote conflict-copy name is already in use: " + conflict);
                if (existingRemoteConflict.Length == 0)
                    await PushSyncFileAsync(peer, link, copy, conflict, "", work.Token);
                await PushSyncFileAsync(peer, link, l, path, r.Sha256, work.Token);
                baseline[path] = l.Sha256;
                baseline[conflict] = r.Sha256;
                changed++;
                conflicts++;
            }
            LocalGuard();
            if (changed > 0 || link.LastSuccessUtc == null || baseline.Count != link.Baseline.Count || baseline.Any(pair => link.Baseline.GetValueOrDefault(pair.Key) != pair.Value))
                Sync.SaveLink(link with
                {
                    Baseline = baseline,
                    LastSuccessUtc = DateTime.UtcNow
                });
            if (changed == 0 && response.SnapshotRevision.Length == 32)
                reconciled[id] = (local.Id, response.SnapshotRevision);
            else
                reconciled.TryRemove(id, out _);
            SetSyncStatus(link, conflicts > 0 ? $"Up to date · {conflicts} conflict copies preserved on both PCs." : changed > 0 ? $"Up to date · {changed} files updated." : "Up to date · watching for changes.");
        }
        catch (Exception error)
        {
            SyncLink? latest = Sync.GetLink(id);
            if (latest != null)
                SetSyncStatus(latest, latest.Enabled ? HumanError(error) + " Waiting to retry." : "Paused. Local files are kept.");
            throw;
        }
        finally { activeSync.TryRemove(id, out _); mutex.Release(); }
    }
    private async Task PushSyncFileAsync(Peer peer, SyncLink link, ManifestEntry source, string path, string expectedRemote, CancellationToken token)
    {
        await using Channel channel = await ConnectSpaceAsync(peer, token);
        using CancellationTokenRegistration close = token.Register(channel.Client.Dispose);
        ManifestEntry entry = source with
        {
            RelativePath = path
        };
        TransferJob job = SpaceJob(peer, link.SpaceId, "Sent", entry);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
        try
        {
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "sync-put",
                SpaceId = link.SpaceId,
                SyncId = link.Id,
                ExpectedHash = expectedRemote,
                Manifest = new()
                {
                    Id = job.Snapshot.Id,
                    Entries = [entry]
                }
            }, work.Token);
            Wire.Require(await Wire.ReadAsync(channel.Stream, work.Token), "accepted");
            await SendSpaceBlobAsync(channel.Stream, entry, job, () => { if (store.Trusted(peer.Fingerprint) == null || !Sync.IsEnabled(link.Id)) throw new OperationCanceledException(); }, work.Token);
            WireMessage ack = await Wire.ReadAsync(channel.Stream, work.Token, 300);
            Wire.Require(ack, "verified");
            if (ack.Sha256 != entry.Sha256)
                throw new ProtocolException("The sync receiver did not verify the original bytes.");
            job.Verified(entry);
            job.State(TransferState.Completed);
        }
        catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
    }
}
