using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lantoss.Core;
using Lantoss.Networking;

internal static partial class Program
{
    private static async Task NativeSyncCasesAsync()
    {
        MetadataStore clientStore = Store("Sync client"), hostStore = Store("Sync host");
        using Identity clientKey = Identity.Ephemeral(), hostKey = Identity.Ephemeral();
        using TransferEngine client = new(clientStore, clientKey, true, false), host = new(hostStore, hostKey, true, false);
        client.Start(Loopback, 0, false);
        host.Start(Loopback, 0, false);
        Peer hostPeer = await client.ConnectDirectAsync(new()
        {
            Fingerprint = hostKey.Fingerprint,
            Name = "Sync host",
            Address = "127.0.0.1",
            Port = host.Port,
            SupportsDirectConnect = true
        });
        await client.SendNoteAsync(hostPeer, "https://example.com/from-client");
        Check(host.Browser.Notes.Snapshot().Notes.Single().Content == "https://example.com/from-client", "A paired PC delivers a link over pinned TLS into the other app's Text & links area");
        Peer clientPeer = hostStore.Peers.Single(p => p.Fingerprint == clientKey.Fingerprint);
        await host.SendNoteAsync(clientPeer, "Reply from host 😀");
        Check(client.Browser.Notes.Snapshot().Notes.Single().Content == "Reply from host 😀", "Native text sharing works in both PC-to-PC directions");
        string hostFolder = Path.Combine(hostStore.Root, "Family"), clientFolder = Path.Combine(clientStore.Root, "Local family");
        Directory.CreateDirectory(hostFolder);
        Directory.CreateDirectory(clientFolder);
        Directory.CreateDirectory(Path.Combine(hostFolder, "Nested", "Empty"));
        await File.WriteAllTextAsync(Path.Combine(hostFolder, "host.txt"), "Host original bytes");
        await File.WriteAllTextAsync(Path.Combine(hostFolder, "same.txt"), "Host same-name bytes");
        await File.WriteAllTextAsync(Path.Combine(clientFolder, "client.txt"), "Client original bytes");
        await File.WriteAllTextAsync(Path.Combine(clientFolder, "same.txt"), "Client same-name bytes");
        SharedSpace space = host.Browser.Spaces.Add(hostFolder);
        RemoteSpace remote = (await client.GetRemoteSpacesAsync(hostPeer)).Single();
        Check(remote.Id == space.Id && !remote.CanUpload && remote.Available, "Native TLS lists actual host spaces without using the browser");
        SpaceEntry[] listed = await client.BrowseRemoteSpaceAsync(hostPeer, space.Id);
        Check(listed.Any(entry => entry.Directory && entry.Name == "Nested") && listed.Any(entry => entry.Name == "host.txt"), "Native shared folders support real directory/file browsing");
        string download = Path.Combine(clientStore.Root, "Downloads");
        Directory.CreateDirectory(download);
        await client.DownloadSpaceFileAsync(hostPeer, space.Id, "host.txt", download);
        await client.DownloadSpaceFileAsync(hostPeer, space.Id, "host.txt", download);
        Check(File.ReadAllText(Path.Combine(download, "host.txt")) == "Host original bytes" && File.Exists(Path.Combine(download, "host (1).txt")), "Native shared-space downloads verify exact bytes and keep both duplicate names");
        bool traversal = false;
        try
        {
            await client.BrowseRemoteSpaceAsync(hostPeer, space.Id, "../");
        }
        catch (IOException) { traversal = true; }
        Check(traversal, "Native shared-folder requests reject root traversal");
        int approvals = 0;
        host.ApproveReceive = (request, _) => { approvals++; return Task.FromResult(new ReceiveDecision(true, clientStore.Root)); };
        bool readOnly = false;
        try
        {
            await client.UploadToSpaceAsync(hostPeer, space.Id, "", [Path.Combine(clientFolder, "client.txt")]);
        }
        catch (IOException) { readOnly = true; }
        Check(readOnly && approvals == 0 && !File.Exists(Path.Combine(hostFolder, "client.txt")), "Native read-only permission denies upload before prompting or writing");
        host.Browser.Spaces.Update(space.Id, SpaceAccess.ReadAndUpload, true);
        await client.UploadToSpaceAsync(hostPeer, space.Id, "Nested", [Path.Combine(clientFolder, "client.txt"), Path.Combine(clientFolder, "same.txt")]);
        Check(approvals == 1 && File.ReadAllText(Path.Combine(hostFolder, "Nested", "client.txt")) == "Client original bytes" && File.Exists(Path.Combine(hostFolder, "Nested", "same.txt")), "A native upload batch is approved once and pinned to its shared subfolder");
        remote = (await client.GetRemoteSpacesAsync(hostPeer)).Single();
        host.ApproveSync = (_, _) => Task.FromResult(false);
        bool declined = false;
        try
        {
            await client.LinkSpaceAsync(hostPeer, remote, clientFolder);
        }
        catch (IOException) { declined = true; }
        Check(declined && client.Sync.Links.Length == 0 && host.Sync.Grants.Length == 0, "Declining automatic sync stores no link or automatic-write grant");
        int syncApprovals = 0;
        host.ApproveSync = (_, _) => { syncApprovals++; return Task.FromResult(true); };
        SyncLink link = await client.LinkSpaceAsync(hostPeer, remote, clientFolder);
        Check(syncApprovals == 1 && new SyncStore(clientStore.Root).Links.Single().Id == link.Id && new SyncStore(hostStore.Root).Granted(link.Id, clientKey.Fingerprint, space.Id), "Automatic sync persists its local link and one-time peer/space-scoped host grant");
        await client.SyncOnceAsync(link.Id);
        string initialConflict = SyncFiles.ConflictPath("same.txt", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Host same-name bytes"))));
        Check(File.ReadAllText(Path.Combine(hostFolder, "same.txt")) == "Client same-name bytes" && File.ReadAllText(Path.Combine(clientFolder, initialConflict)) == "Host same-name bytes" && File.ReadAllText(Path.Combine(hostFolder, initialConflict)) == "Host same-name bytes", "Initial same-name differences retain both original contents on both PCs");
        Check(File.Exists(Path.Combine(hostFolder, "client.txt")) && File.Exists(Path.Combine(clientFolder, "host.txt")) && Directory.Exists(Path.Combine(clientFolder, "Nested", "Empty")), "First sync merges files in both directions and preserves empty nested folders");
        await File.WriteAllTextAsync(Path.Combine(clientFolder, "client.txt"), "Client changed this document");
        await client.SyncOnceAsync(link.Id);
        Check(File.ReadAllText(Path.Combine(hostFolder, "client.txt")) == "Client changed this document" && syncApprovals == 1, "Client edits update the host without another approval after the folder grant");
        await File.WriteAllTextAsync(Path.Combine(hostFolder, "host.txt"), "Host edited the remote file");
        await client.SyncOnceAsync(link.Id);
        Check(File.ReadAllText(Path.Combine(clientFolder, "host.txt")) == "Host edited the remote file", "Host edits automatically reconcile back into the linked local folder");
        Check(Directory.EnumerateFiles(Path.Combine(hostFolder, ".flowshare-versions"), "*.bak", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == "Client original bytes") && Directory.EnumerateFiles(Path.Combine(clientFolder, ".flowshare-versions"), "*.bak", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == "Host original bytes"), "Replacements retain the actual previous bytes in local version backups");
        await File.WriteAllTextAsync(Path.Combine(clientFolder, "same.txt"), "Both changed: client");
        await File.WriteAllTextAsync(Path.Combine(hostFolder, "same.txt"), "Both changed: host");
        await client.SyncOnceAsync(link.Id);
        string conflict = SyncFiles.ConflictPath("same.txt", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Both changed: host"))));
        Check(File.ReadAllText(Path.Combine(hostFolder, "same.txt")) == "Both changed: client" && File.ReadAllText(Path.Combine(hostFolder, conflict)) == "Both changed: host" && File.Exists(Path.Combine(clientFolder, conflict)), "Simultaneous edits preserve a deterministic conflict copy on both PCs");
        File.Delete(Path.Combine(clientFolder, "host.txt"));
        await client.SyncOnceAsync(link.Id);
        Check(File.ReadAllText(Path.Combine(clientFolder, "host.txt")) == "Host edited the remote file" && File.Exists(Path.Combine(hostFolder, "host.txt")), "Sync never propagates a deletion and restores the remaining safe copy");
        using SyncScanner snapshotScanner = new();
        Manifest snapshot = await snapshotScanner.ScanAsync(hostFolder, default);
        Check(snapshot.Entries.All(entry => !entry.RelativePath.StartsWith(".flowshare-", StringComparison.OrdinalIgnoreCase)), "Sync snapshots exclude private staging and previous-version backups");
        Manifest manualSelection = await FileCatalog.BuildAsync([hostFolder], Guid.NewGuid().ToString("N"), false, default);
        Check(manualSelection.FileCount > 0 && manualSelection.Entries.All(entry => !entry.RelativePath.Contains("/.flowshare-", StringComparison.OrdinalIgnoreCase)), "A synced folder can still be shared manually without exposing its internal version backups");
        string guarded = Path.Combine(clientFolder, "guarded.txt"), temp = Path.Combine(clientFolder, ".flowshare-cas.part");
        await File.WriteAllTextAsync(guarded, "New user edit");
        await File.WriteAllTextAsync(temp, "Incoming older revision");
        bool guardedWrite = false;
        try
        {
            await SyncFiles.CommitAsync(temp, clientFolder, "guarded.txt", new string('A', 64), default);
        }
        catch (IOException) { guardedWrite = true; }
        Check(guardedWrite && File.ReadAllText(guarded) == "New user edit", "A changed destination revision prevents a stale sync replacement");
        File.Delete(temp);
        client.PauseSync(link.Id, true);
        bool paused = false;
        try
        {
            await client.SyncOnceAsync(link.Id);
        }
        catch (OperationCanceledException) { paused = true; }
        Check(paused && !new SyncStore(clientStore.Root).Links.Single().Enabled, "Pausing sync persists and prevents a further reconciliation");
        client.PauseSync(link.Id, false);
        host.Sync.Revoke(link.Id, clientKey.Fingerprint);
        await File.WriteAllTextAsync(Path.Combine(clientFolder, "revoked.txt"), "No automatic write grant");
        bool revoked = false;
        try
        {
            await client.SyncOnceAsync(link.Id);
        }
        catch (IOException) { revoked = true; }
        Check(revoked && !File.Exists(Path.Combine(hostFolder, "revoked.txt")), "Revoking a host sync grant prevents future automatic writes");
        host.Sync.Grant(new(link.Id, clientKey.Fingerprint, space.Id));
        host.Browser.Spaces.Update(space.Id, SpaceAccess.ReadOnly, true);
        bool downgraded = false;
        try
        {
            await client.SyncOnceAsync(link.Id);
        }
        catch (IOException) { downgraded = true; }
        Check(downgraded && !File.Exists(Path.Combine(hostFolder, "revoked.txt")), "Changing a shared space to read only also denies automatic sync writes");
        host.Browser.Spaces.Update(space.Id, SpaceAccess.ReadAndUpload, true);
        string revision = await SyncFiles.HashAsync(Path.Combine(hostFolder, "client.txt"), default);
        string goodHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("good")));
        var corruptChannel = await RawAsync(host, clientKey, hostKey);
        using (corruptChannel.Client)
        await using (corruptChannel.Stream)
        {
            await Wire.WriteAsync(corruptChannel.Stream, new()
            {
                Kind = "sync-put",
                SpaceId = space.Id,
                SyncId = link.Id,
                ExpectedHash = revision,
                Manifest = One("client.txt", 4, goodHash)
            }, default);
            Wire.Require(await Wire.ReadAsync(corruptChannel.Stream, default), "accepted");
            await corruptChannel.Stream.WriteAsync(Encoding.UTF8.GetBytes("evil"));
            bool corrupt = false;
            try
            {
                await Wire.ReadAsync(corruptChannel.Stream, default);
            }
            catch (IOException) { corrupt = true; }
            Check(corrupt && await SyncFiles.HashAsync(Path.Combine(hostFolder, "client.txt"), default) == revision, "A corrupt automatic upload never replaces the existing good revision");
        }
        var ungranted = await RawAsync(host, clientKey, hostKey);
        using (ungranted.Client)
        await using (ungranted.Stream)
        {
            await Wire.WriteAsync(ungranted.Stream, new()
            {
                Kind = "sync-mkdir",
                SpaceId = space.Id,
                SyncId = Guid.NewGuid().ToString("N"),
                RelativePath = "Ungrantable"
            }, default);
            bool scoped = false;
            try
            {
                await Wire.ReadAsync(ungranted.Stream, default);
            }
            catch (IOException) { scoped = true; }
            Check(scoped && !Directory.Exists(Path.Combine(hostFolder, "Ungrantable")), "A paired PC cannot create folders using another or missing sync grant");
        }
        var partial = await RawAsync(host, clientKey, hostKey);
        Manifest partialManifest = One("partial-sync.bin", 1048576, new string('A', 64));
        await Wire.WriteAsync(partial.Stream, new()
        {
            Kind = "sync-put",
            SpaceId = space.Id,
            SyncId = link.Id,
            Manifest = partialManifest
        }, default);
        Wire.Require(await Wire.ReadAsync(partial.Stream, default), "accepted");
        await partial.Stream.WriteAsync(new byte[65536]);
        partial.Client.Dispose();
        try
        {
            await partial.Stream.DisposeAsync();
        }
        catch (IOException) { }
        await Wait(() => host.Transfers.Records.Any(record => record.Files.Any(file => file.RelativePath == "partial-sync.bin") && record.Terminal));
        Check(!File.Exists(Path.Combine(hostFolder, "partial-sync.bin")) && !Directory.EnumerateFiles(hostFolder, "*.part", SearchOption.AllDirectories).Any(), "An interrupted automatic upload cleans staging and never commits a partial file");
        host.Forget(hostStore.Trusted(clientKey.Fingerprint)!, false);
        Check(host.Sync.Grants.All(grant => grant.PeerKey != clientKey.Fingerprint), "Unpairing a PC also removes its persisted automatic-sync grants");
        client.Forget(clientStore.Trusted(hostKey.Fingerprint)!, false);
        Check(!client.Sync.Links.Single().Enabled, "Unpairing the host pauses its persisted local sync link");
        client.RemoveSync(link.Id);
        Check(client.Sync.Links.Length == 0 && File.Exists(Path.Combine(clientFolder, "same.txt")), "Removing a sync link keeps every local file");
        string unavailableFolder = Path.Combine(clientStore.Root, "Disconnected sync root");
        Directory.CreateDirectory(unavailableFolder);
        SyncLink unavailable = link with
        {
            Id = Guid.NewGuid().ToString("N"),
            Folder = unavailableFolder,
            Baseline = []
        };
        client.Sync.SaveLink(unavailable);
        Directory.Delete(unavailableFolder);
        client.PauseSync(unavailable.Id, true);
        Check(!new SyncStore(clientStore.Root).Links.Single().Enabled && !Directory.Exists(unavailableFolder), "An unavailable sync drive/folder can still be paused without recreating its root");
        client.RemoveSync(unavailable.Id);
        NetworkAdapter subnet = new("LAN", IPAddress.Parse("192.168.12.8"), IPAddress.Parse("255.255.255.0"), true);
        Check(LanDiscovery.Broadcast(subnet).ToString() == "192.168.12.255" && clientStore.Preferences.AutoConnect, "LAN fallback uses the actual subnet broadcast and automatic direct connection defaults on");
        await AutomaticSyncCasesAsync();
        using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
        await client.ShutdownAsync(shutdown.Token);
        await host.ShutdownAsync(shutdown.Token);
    }
    private static async Task AutomaticSyncCasesAsync()
    {
        MetadataStore first = Store("Automatic sync client"), second = Store("Automatic sync host");
        using Identity a = Identity.Ephemeral(), b = Identity.Ephemeral();
        using TransferEngine client = new(first, a, true), host = new(second, b, true);
        client.Start(Loopback, 0, false);
        host.Start(Loopback, 0, false);
        Peer peer = await client.ConnectDirectAsync(new()
        {
            Fingerprint = b.Fingerprint,
            Name = "Automatic host",
            Address = "127.0.0.1",
            Port = host.Port,
            SupportsDirectConnect = true
        });
        string local = Path.Combine(first.Root, "Auto folder"), remote = Path.Combine(second.Root, "Auto folder");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(remote);
        SharedSpace space = host.Browser.Spaces.Add(remote);
        host.Browser.Spaces.Update(space.Id, SpaceAccess.ReadAndUpload, true);
        host.ApproveSync = (_, _) => Task.FromResult(true);
        SyncLink link = await client.LinkSpaceAsync(peer, (await client.GetRemoteSpacesAsync(peer)).Single(), local);
        await File.WriteAllTextAsync(Path.Combine(local, "automatic.txt"), "No Sync now action was invoked");
        await Wait(() => File.Exists(Path.Combine(remote, "automatic.txt")), 25);
        Check(File.ReadAllText(Path.Combine(remote, "automatic.txt")) == "No Sync now action was invoked", "The background loop syncs a new file without any manual refresh or sync action");
        using (CancellationTokenSource stopHost = new(TimeSpan.FromSeconds(10)))
            await host.ShutdownAsync(stopHost.Token);
        await File.WriteAllTextAsync(Path.Combine(local, "after-reconnect.txt"), "Queued while host was offline");
        await Task.Delay(5500);
        int port = peer.Port;
        using TransferEngine restartedHost = new(new MetadataStore(second.Root), b, true);
        restartedHost.Start(Loopback, port, false);
        await Wait(() => File.Exists(Path.Combine(remote, "after-reconnect.txt")), 35);
        Check(File.ReadAllText(Path.Combine(remote, "after-reconnect.txt")) == "Queued while host was offline", "An offline peer is retried and pending edits sync after a full host-engine restart");
        int clientPort = client.Port;
        using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
        await client.ShutdownAsync(shutdown.Token);
        using TransferEngine restartedClient = new(new MetadataStore(first.Root), a, true);
        restartedClient.Start(Loopback, clientPort, false);
        await File.WriteAllTextAsync(Path.Combine(remote, "after-client-restart.txt"), "No new setup or approval");
        await Wait(() => File.Exists(Path.Combine(local, "after-client-restart.txt")), 25);
        Check(File.ReadAllText(Path.Combine(local, "after-client-restart.txt")) == "No new setup or approval", "Reopening the syncing PC resumes its persisted link and pulls changes without another approval");
        restartedClient.RemoveSync(link.Id);
        using CancellationTokenSource finalStop = new(TimeSpan.FromSeconds(10));
        await restartedClient.ShutdownAsync(finalStop.Token);
        await restartedHost.ShutdownAsync(finalStop.Token);
    }
}
