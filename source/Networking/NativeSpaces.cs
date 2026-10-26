using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Lantoss.Core;

namespace Lantoss.Networking;

/// <summary>
/// Runs shared-folder browsing, uploads and sync RPCs over authenticated native TLS. Recheck the paired key, space and optional sync grant for each operation and before commit.
/// </summary>
public sealed partial class TransferEngine
{
    public SyncStore Sync
    {
        get;
    }
    public Func<SyncApproval, CancellationToken, Task<bool>>? ApproveSync
    {
        get; set;
    }
    private readonly SyncScanner spaceScanner = new();
    private readonly SemaphoreSlim snapshotSlot = new(1, 1);
    private readonly SemaphoreSlim spaceClients = new(3, 3);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> spaceWrites = new();
    private async Task<Channel> ConnectSpaceAsync(Peer peer, CancellationToken token)
    {
        await spaceClients.WaitAsync(token);
        try
        {
            Channel channel = await ConnectPeerAsync(peer, token);
            channel.Closed = () => spaceClients.Release();
            return channel;
        }
        catch { spaceClients.Release(); throw; }
    }
    private void GuardSpace(string id, string root, string key, bool upload, string syncId = "")
    {
        if (store.Trusted(key) == null)
            throw new AuthenticationException("The PC's access was revoked.");
        Browser.Spaces.CheckAccess(id, root, upload);
        if (syncId.Length > 0 && !Sync.Granted(syncId, key, id))
            throw new ProtocolException("Automatic sync permission was revoked. Request approval again.");
    }
    public async Task<RemoteSpace[]> GetRemoteSpacesAsync(Peer peer, CancellationToken token = default)
    {
        WireMessage result = await SpaceRequestAsync(peer, new()
        {
            Kind = "space-list"
        }, token);
        Wire.Require(result, "spaces");
        foreach (RemoteSpace space in result.Spaces)
            if (space is null || !Guid.TryParseExact(space.Id, "N", out _) || space.Name is null || space.Name.Length is < 1 or > 64)
                throw new ProtocolException("Invalid shared folder listing.");
        return result.Spaces;
    }
    public async Task<SpaceEntry[]> BrowseRemoteSpaceAsync(Peer peer, string spaceId, string folder = "", CancellationToken token = default)
    {
        WireMessage result = await SpaceRequestAsync(peer, new()
        {
            Kind = "space-browse",
            SpaceId = spaceId,
            RelativePath = folder
        }, token);
        Wire.Require(result, "entries");
        foreach (SpaceEntry entry in result.Entries)
        {
            if (entry is null || entry.Name is null || entry.Path is null || entry.Size < 0)
                throw new ProtocolException("Invalid shared folder entry.");
            FileCatalog.ValidateRelative(entry.Path);
            FileCatalog.ValidateSegment(entry.Name);
        }
        return result.Entries;
    }
    private async Task<WireMessage> SpaceRequestAsync(Peer peer, WireMessage message, CancellationToken token)
    {
        await using Channel channel = await ConnectSpaceAsync(peer, token);
        using CancellationTokenRegistration close = token.Register(channel.Client.Dispose);
        await Wire.WriteAsync(channel.Stream, message, token);
        return await Wire.ReadAsync(channel.Stream, token, 125);
    }
    public async Task DownloadSpaceFileAsync(Peer peer, string spaceId, string path, string folder, CancellationToken token = default)
        => await FetchSpaceFileAsync(peer, spaceId, path, folder, Path.GetFileName(path), null, "", token);
    private async Task FetchSpaceFileAsync(Peer peer, string spaceId, string path, string folder, string destination, string? expectedLocal, string expectedRemote, CancellationToken token, string syncId = "")
    {
        await using Channel channel = await ConnectSpaceAsync(peer, token);
        using CancellationTokenRegistration close = token.Register(channel.Client.Dispose);
        await Wire.WriteAsync(channel.Stream, new()
        {
            Kind = "space-get",
            SpaceId = spaceId,
            RelativePath = path,
            Sha256 = expectedRemote,
            SyncId = syncId
        }, token);
        WireMessage header = await Wire.ReadAsync(channel.Stream, token, 125);
        Wire.Require(header, "space-file");
        ManifestEntry entry = SingleFile(header.Manifest);
        if (entry.RelativePath != path || (expectedRemote.Length > 0 && !entry.Sha256.Equals(expectedRemote, StringComparison.OrdinalIgnoreCase)))
            throw new ProtocolException("The remote source changed. Sync will retry.");
        TransferJob job = SpaceJob(peer, spaceId, "Received", entry);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
        try
        {
            await ReceiveSpaceBlobAsync(channel.Stream, entry, folder, destination, expectedLocal, () => { if (store.Trusted(peer.Fingerprint) == null) throw new AuthenticationException(); }, job, work.Token);
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "verified",
                Sha256 = entry.Sha256
            }, work.Token);
            Wire.Require(await Wire.ReadAsync(channel.Stream, work.Token), "complete");
            job.State(TransferState.Completed);
        }
        catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
    }
    public async Task UploadToSpaceAsync(Peer peer, string spaceId, string folder, IEnumerable<string> paths, CancellationToken token = default)
    {
        Manifest manifest = await FileCatalog.BuildAsync(paths, Guid.NewGuid().ToString("N"), true, token);
        FileCatalog.ValidateManifest(manifest);
        await using Channel channel = await ConnectSpaceAsync(peer, token);
        using CancellationTokenRegistration close = token.Register(channel.Client.Dispose);
        TransferJob job = Transfers.Add(new()
        {
            Id = manifest.Id,
            Direction = "Sent",
            PeerName = peer.Name,
            PeerFingerprint = peer.Fingerprint,
            SpaceId = spaceId,
            State = TransferState.AwaitingApproval
        });
        job.Initialize(manifest);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
        try
        {
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "space-upload",
                SpaceId = spaceId,
                RelativePath = folder,
                Manifest = manifest
            }, work.Token);
            WireMessage approval = await Wire.ReadAsync(channel.Stream, work.Token, 125);
            if (approval.Kind == "declined")
            {
                job.State(TransferState.Declined);
                return;
            }
            Wire.Require(approval, "accepted");
            int index = 0;
            foreach (ManifestEntry entry in manifest.Entries)
            {
                if (!entry.Directory)
                {
                    await Wire.WriteAsync(channel.Stream, new()
                    {
                        Kind = "file",
                        Index = index
                    }, work.Token);
                    await SendSpaceBlobAsync(channel.Stream, entry, job, () => { if (store.Trusted(peer.Fingerprint) == null) throw new AuthenticationException(); }, work.Token);
                    WireMessage ack = await Wire.ReadAsync(channel.Stream, work.Token, 300);
                    Wire.Require(ack, "verified");
                    if (!ack.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new ProtocolException("The received file checksum did not match.");
                    job.Verified(entry);
                }
                index++;
            }
            await Wire.WriteAsync(channel.Stream, new()
            {
                Kind = "finish"
            }, work.Token);
            Wire.Require(await Wire.ReadAsync(channel.Stream, work.Token), "complete");
            job.State(TransferState.Completed);
        }
        catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
    }
    private static ManifestEntry SingleFile(Manifest? manifest)
    {
        if (manifest?.Entries is not { Count: 1 })
            throw new ProtocolException("Expected one file.");
        ManifestEntry entry = manifest.Entries[0];
        if (entry is null || entry.RelativePath is null || entry.Directory || entry.Sha256 is null || entry.Sha256.Length != 64 || !entry.Sha256.All(char.IsAsciiHexDigit) || entry.Size < 0 || entry.Size > FileCatalog.MaxFileBytes)
            throw new ProtocolException("Invalid file metadata.");
        FileCatalog.ValidateRelative(entry.RelativePath);
        return entry;
    }
    private TransferJob SpaceJob(Peer peer, string spaceId, string direction, ManifestEntry entry)
    {
        TransferJob job = Transfers.Add(new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Direction = direction,
            PeerName = peer.Name,
            PeerFingerprint = peer.Fingerprint,
            SpaceId = spaceId,
            State = direction == "Sent" ? TransferState.Sending : TransferState.Receiving
        });
        job.Initialize(new()
        {
            Id = job.Snapshot.Id,
            Entries = [entry]
        });
        return job;
    }
    private async Task ServeSpaceAsync(Stream stream, TcpClient client, Peer peer, WireMessage hello, CancellationToken token)
    {
        if (hello.Kind == "space-list")
        {
            await Wire.WriteAsync(stream, new()
            {
                Kind = "spaces",
                Spaces = Browser.Spaces.Spaces.Where(space => space.Enabled).Select(space => new RemoteSpace(space.Id, space.Name, space.Access == SpaceAccess.ReadAndUpload, SpaceCatalog.Available(space))).ToArray()
            }, token);
            return;
        }
        SharedSpace space = Browser.Spaces.Get(hello.SpaceId, hello.Kind is "space-upload" or "sync-put" or "sync-mkdir" or "sync-grant");
        void Guard() => GuardSpace(space.Id, space.Folder, peer.Fingerprint, hello.Kind is "space-upload" or "sync-put" or "sync-mkdir", hello.Kind == "sync-grant" ? "" : hello.SyncId);
        Guard();
        switch (hello.Kind)
        {
            case "space-browse":
                await Wire.WriteAsync(stream, new()
                {
                    Kind = "entries",
                    Entries = Browser.Spaces.List(space.Id, hello.RelativePath)
                }, token);
                return;
            case "space-snapshot":
                if (!await snapshotSlot.WaitAsync(0, token))
                    throw new ProtocolException("Folder scanning is busy. Sync will retry.");
                try
                {
                    Manifest snapshot = await spaceScanner.ScanAsync(space.Folder, token, hello.KnownRevision.Length == 0);
                    Guard();
                    await Wire.WriteAsync(stream, new()
                    {
                        Kind = hello.KnownRevision == snapshot.Id ? "snapshot-unchanged" : "snapshot",
                        SnapshotRevision = snapshot.Id,
                        Manifest = hello.KnownRevision == snapshot.Id ? null : snapshot
                    }, token);
                }
                finally { snapshotSlot.Release(); }
                return;
            case "sync-grant":
                if (!Guid.TryParseExact(hello.SyncId, "N", out _))
                    throw new ProtocolException("Invalid sync request.");
                if (!Sync.Granted(hello.SyncId, peer.Fingerprint, space.Id))
                {
                    using CancellationTokenSource approval = CancellationTokenSource.CreateLinkedTokenSource(token);
                    approval.CancelAfter(TimeSpan.FromMinutes(2));
                    if (ApproveSync == null || !await ApproveSync(new(peer.Name, peer.Fingerprint, space), approval.Token))
                        throw new ProtocolException("Automatic sync was declined on the host PC.");
                    Guard();
                    Browser.Spaces.Get(space.Id, true);
                    Sync.Grant(new(hello.SyncId, peer.Fingerprint, space.Id));
                }
                await Wire.WriteAsync(stream, new()
                {
                    Kind = "sync-granted"
                }, token);
                return;
            case "sync-mkdir":
                if (hello.SyncId.Length == 0)
                    throw new ProtocolException("Sync permission is required.");
                SyncFiles.MakeDirectory(space.Folder, hello.RelativePath);
                Guard();
                await Wire.WriteAsync(stream, new()
                {
                    Kind = "complete"
                }, token);
                return;
            case "space-get":
                {
                    string path = Browser.Spaces.Resolve(space.Id, hello.RelativePath, false);
                    FileInfo info = new(path);
                    string hash = await SyncFiles.HashAsync(path, token);
                    if (hello.Sha256.Length > 0 && !hash.Equals(hello.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new ProtocolException("The source changed. Sync will retry.");
                    ManifestEntry entry = new()
                    {
                        RelativePath = hello.RelativePath,
                        Size = info.Length,
                        Sha256 = hash,
                        SourcePath = path,
                        SourceModifiedUtc = info.LastWriteTimeUtc
                    };
                    TransferJob job = SpaceJob(peer, space.Id, "Sent", entry);
                    using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
                    try
                    {
                        await Wire.WriteAsync(stream, new()
                        {
                            Kind = "space-file",
                            Manifest = new()
                            {
                                Id = job.Snapshot.Id,
                                Entries = [entry]
                            }
                        }, work.Token);
                        await SendSpaceBlobAsync(stream, entry, job, Guard, work.Token);
                        WireMessage ack = await Wire.ReadAsync(stream, work.Token, 300);
                        Wire.Require(ack, "verified");
                        if (ack.Sha256 != hash)
                            throw new ProtocolException("The recipient did not verify the original bytes.");
                        Guard();
                        job.Verified(entry);
                        await Wire.WriteAsync(stream, new()
                        {
                            Kind = "complete"
                        }, work.Token);
                        job.State(TransferState.Completed);
                    }
                    catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
                    return;
                }
            case "space-upload":
                await ReceiveSpaceBatchAsync(stream, peer, space, hello, Guard, token);
                return;
            case "sync-put":
                {
                    if (hello.SyncId.Length == 0 || (hello.ExpectedHash.Length != 0 && (hello.ExpectedHash.Length != 64 || !hello.ExpectedHash.All(char.IsAsciiHexDigit))))
                        throw new ProtocolException("Invalid sync revision.");
                    ManifestEntry entry = SingleFile(hello.Manifest);
                    SemaphoreSlim mutex = spaceWrites.GetOrAdd(space.Id, _ => new(1, 1));
                    await mutex.WaitAsync(token);
                    try
                    {
                        Guard();
                        if (await SyncFiles.HashAsync(SyncFiles.PathIn(space.Folder, entry.RelativePath), token) != hello.ExpectedHash)
                            throw new ProtocolException("The remote file changed. Sync will reconcile again.");
                        TransferJob job = SpaceJob(peer, space.Id, "Received", entry);
                        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
                        try
                        {
                            await Wire.WriteAsync(stream, new()
                            {
                                Kind = "accepted"
                            }, work.Token);
                            await ReceiveSpaceBlobAsync(stream, entry, space.Folder, entry.RelativePath, hello.ExpectedHash, Guard, job, work.Token);
                            await Wire.WriteAsync(stream, new()
                            {
                                Kind = "verified",
                                Sha256 = entry.Sha256
                            }, work.Token);
                            job.State(TransferState.Completed);
                        }
                        catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
                    }
                    finally { mutex.Release(); }
                    return;
                }
            default:
                throw new ProtocolException("Update Lantoss on both PCs for shared folders and sync.");
        }
    }
    private async Task ReceiveSpaceBatchAsync(Stream stream, Peer peer, SharedSpace space, WireMessage hello, Action guard, CancellationToken token)
    {
        Manifest manifest = hello.Manifest ?? throw new ProtocolException("Missing upload manifest.");
        FileCatalog.ValidateManifest(manifest);
        string folder = Browser.Spaces.Resolve(space.Id, hello.RelativePath, true, true);
        TransferJob job = Transfers.Add(new()
        {
            Id = manifest.Id,
            Direction = "Received",
            PeerName = peer.Name,
            PeerFingerprint = peer.Fingerprint,
            SpaceId = space.Id,
            State = TransferState.AwaitingApproval
        });
        job.Initialize(manifest);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token, job.Cancellation.Token);
        try
        {
            using CancellationTokenSource approval = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
            approval.CancelAfter(TimeSpan.FromMinutes(2));
            ReceiveDecision decision = ApproveReceive == null ? new(false, folder) : await ApproveReceive(new(peer.Name + " → " + space.Name, peer.Fingerprint, manifest, folder, FileCatalog.FreeSpace(folder), peer.KeyVerified, false, true), approval.Token);
            if (!decision.Accepted)
            {
                job.State(TransferState.Declined);
                await Wire.WriteAsync(stream, new()
                {
                    Kind = "declined"
                }, work.Token);
                return;
            }
            guard();
            ReceivePlan plan = new(manifest, folder, DuplicatePolicy.KeepBoth);
            if (FileCatalog.FreeSpace(folder) < plan.Bytes + 64L * 1024 * 1024)
                throw new IOException("Not enough free space.");
            plan.CreateDirectories();
            job.Update(record => record with { Destination = folder });
            await Wire.WriteAsync(stream, new()
            {
                Kind = "accepted"
            }, work.Token);
            foreach (ReceiveItem item in plan.Items.Where(item => !item.Entry.Directory))
            {
                WireMessage header = await Wire.ReadAsync(stream, work.Token);
                Wire.Require(header, "file");
                if (header.Index != item.Index)
                    throw new ProtocolException("Unexpected upload file order.");
                string relative = Path.GetRelativePath(folder, item.Destination).Replace('\\', '/');
                await ReceiveSpaceBlobAsync(stream, item.Entry, folder, relative, null, guard, job, work.Token);
                await Wire.WriteAsync(stream, new()
                {
                    Kind = "verified",
                    Sha256 = item.Entry.Sha256
                }, work.Token);
            }
            Wire.Require(await Wire.ReadAsync(stream, work.Token), "finish");
            guard();
            await Wire.WriteAsync(stream, new()
            {
                Kind = "complete"
            }, work.Token);
            job.State(TransferState.Completed);
        }
        catch (Exception error) { job.State(work.IsCancellationRequested ? TransferState.Cancelled : TransferState.Failed, HumanError(error)); throw; }
    }
    private static async Task SendSpaceBlobAsync(Stream stream, ManifestEntry entry, TransferJob job, Action guard, CancellationToken token)
    {
        FileCatalog.CheckSafePath(entry.SourcePath);
        await using FileStream input = new(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != entry.Size || File.GetLastWriteTimeUtc(entry.SourcePath) != entry.SourceModifiedUtc)
            throw new IOException("The source is still changing. Retry shortly.");
        job.StartFile(entry, TransferState.Sending);
        byte[] buffer = new byte[65536];
        long left = entry.Size;
        while (left > 0)
        {
            guard();
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(left, buffer.Length)), token);
            if (read == 0)
                throw new EndOfStreamException();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            left -= read;
            job.Advance(read);
        }
        await stream.FlushAsync(token);
    }
    private static async Task ReceiveSpaceBlobAsync(Stream stream, ManifestEntry entry, string root, string relative, string? expected, Action guard, TransferJob job, CancellationToken token)
    {
        FileCatalog.CheckSafeDirectory(root);
        if (!Directory.Exists(root))
            throw new IOException("The destination folder is unavailable.");
        string target = SyncFiles.PathIn(root, relative);
        string parent = Path.GetDirectoryName(target)!;
        FileCatalog.CheckSafeDirectory(parent);
        Directory.CreateDirectory(parent);
        if (FileCatalog.FreeSpace(root) < entry.Size + 64L * 1024 * 1024)
            throw new IOException("Not enough free space.");
        string temp = Path.Combine(parent, ".flowshare-" + Guid.NewGuid().ToString("N") + ".part");
        job.Update(record => record with { Destination = root });
        job.StartFile(entry, TransferState.Receiving);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (FileStream output = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[65536];
                long left = entry.Size;
                while (left > 0)
                {
                    guard();
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(left, buffer.Length)), timeout.Token);
                    if (read == 0)
                        throw new EndOfStreamException();
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    left -= read;
                    job.Advance(read);
                }
                await output.FlushAsync(token);
                output.Flush(true);
            }
            string digest = Convert.ToHexString(hash.GetHashAndReset());
            if (!digest.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ProtocolException("Integrity verification failed. No file was replaced.");
            token.ThrowIfCancellationRequested();
            guard();
            FileCatalog.CheckSafeDirectory(parent);
            if (expected != null)
                await SyncFiles.CommitAsync(temp, root, relative, expected, token);
            else
            {
                for (int attempt = 0; ; attempt++)
                {
                    if (attempt > 10000)
                        throw new IOException("Too many duplicate filenames.");
                    string destination = FileCatalog.Unique(target);
                    try
                    {
                        File.Move(temp, destination, false);
                        break;
                    }
                    catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
                }
            }
            job.Verified(entry);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
