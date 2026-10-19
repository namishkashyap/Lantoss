using System.Diagnostics;

namespace Lantoss.Core;

public sealed class TransferJob : IDisposable
{
    private readonly object gate = new();
    private TransferRecord record;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastSampleBytes, currentFileBytes; private double lastSampleAt, lastEventAt;
    public CancellationTokenSource Cancellation { get; } = new();
    public event Action<TransferRecord>? Changed;
    public TransferJob(TransferRecord initial)
    {
        record = initial;
    }
    public TransferRecord Snapshot
    {
        get
        {
            lock (gate)
                return record with
                {
                    Files = [.. record.Files]
                };
        }
    }
    public void Update(Func<TransferRecord, TransferRecord> change, bool force = true)
    {
        TransferRecord snapshot;
        bool publish;
        lock (gate)
        {
            if (record.Terminal)
                return;
            record = change(record) with
            {
                UpdatedUtc = DateTime.UtcNow
            };
            if (record.Terminal)
                record = record with
                {
                    BytesPerSecond = 0
                };
            publish = force || clock.Elapsed.TotalSeconds - lastEventAt >= .2;
            if (!publish)
                return;
            lastEventAt = clock.Elapsed.TotalSeconds;
            snapshot = record with
            {
                Files = [.. record.Files]
            };
        }
        Changed?.Invoke(snapshot);
    }
    public void Initialize(Manifest manifest, long? transferBytes = null)
    {
        Update(r => r with
        {
            Title = manifest.Entries.Count == 1 ? manifest.Entries[0].RelativePath : $"{manifest.FileCount} files · {manifest.FolderCount} folders",
            TotalBytes = transferBytes ?? manifest.TotalBytes,
            FileCount = manifest.FileCount,
            FolderCount = manifest.FolderCount,
            Files = manifest.Entries.Where(e => !e.Directory).Select(e => new TransferFile { RelativePath = e.RelativePath, Size = e.Size }).ToList()
        });
    }
    public void State(TransferState state, string error = "") => Update(r => r with { State = state, Error = error }, state != TransferState.Verifying);
    public void StartFile(ManifestEntry entry, TransferState state)
    {
        lock (gate)
        {
            lastSampleAt = clock.Elapsed.TotalSeconds;
            lastSampleBytes = record.TransferredBytes;
            currentFileBytes = 0;
        }
        Update(r => r with { CurrentFile = entry.RelativePath, State = state, BytesPerSecond = 0 }, false);
    }
    public void Advance(int bytes)
    {
        Update(r =>
        {
            long total = checked(r.TransferredBytes + bytes);
            double now = clock.Elapsed.TotalSeconds;
            double speed = r.BytesPerSecond;
            if (now - lastSampleAt >= .3)
            {
                speed = (total - lastSampleBytes) / (now - lastSampleAt);
                lastSampleAt = now;
                lastSampleBytes = total;
            }
            currentFileBytes += bytes;
            List<TransferFile> files = r.Files;
            if (now - lastEventAt >= .2)
            {
                files = [.. r.Files];
                int index = files.FindIndex(f => f.RelativePath == r.CurrentFile);
                if (index >= 0)
                    files[index] = files[index] with
                    {
                        Bytes = currentFileBytes,
                        Status = r.State.ToString()
                    };
            }
            return r with
            {
                TransferredBytes = total,
                BytesPerSecond = speed,
                Files = files
            };
        }, false);
    }
    public void Verified(ManifestEntry entry, bool skipped = false)
    {
        Update(r =>
        {
            List<TransferFile> files = [.. r.Files];
            int index = files.FindIndex(f => f.RelativePath == entry.RelativePath);
            if (index >= 0)
                files[index] = files[index] with
                {
                    Status = skipped ? "Skipped" : "SHA-256 verified",
                    Bytes = skipped ? 0 : entry.Size
                };
            return r with
            {
                VerifiedFiles = r.VerifiedFiles + (skipped ? 0 : 1),
                SkippedFiles = r.SkippedFiles + (skipped ? 1 : 0),
                Files = files
            };
        }, false);
    }
    public void Cancel()
    {
        Cancellation.Cancel();
        State(TransferState.Cancelled, "Cancelled by you. Verified files already received are kept.");
    }
    public void Dispose() => Cancellation.Dispose();
}

/// <summary>
/// Publishes immutable progress snapshots to the UI and persists bounded history. File contents remain at their destination, not in this registry.
/// </summary>
public sealed class TransferRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, TransferJob> jobs = [];
    private readonly Dictionary<string, TransferRecord> history;
    private readonly MetadataStore store;
    private DateTime lastSave;
    private bool savePending, writing;
    private Task persistence = Task.CompletedTask;
    public event Action<TransferRecord>? Changed;
    public event Action<Exception>? PersistenceFailed;
    public TransferRegistry(MetadataStore store)
    {
        this.store = store;
        history = store.History.ToDictionary(r => r.Id + r.Direction);
    }
    public TransferRecord[] Records
    {
        get
        {
            lock (gate)
                return history.Values.OrderByDescending(r => r.CreatedUtc).ToArray();
        }
    }
    public TransferJob Add(TransferRecord record)
    {
        lock (gate)
        {
            // Direction is included so a loopback test cannot collide with its sender record.
            string key = record.Id + record.Direction;
            if (jobs.ContainsKey(key))
                throw new InvalidDataException("This transfer is already active.");
            TransferJob job = new(record);
            jobs.Add(key, job);
            history[key] = record;
            job.Changed += OnChanged;
            Save();
            Changed?.Invoke(record);
            return job;
        }
    }
    private void OnChanged(TransferRecord record)
    {
        lock (gate)
        {
            history[record.Id + record.Direction] = record;
            if (record.Terminal || DateTime.UtcNow - lastSave >= TimeSpan.FromSeconds(2))
                Save();
        }
        Changed?.Invoke(record);
    }
    private void Save()
    {
        savePending = true;
        lastSave = DateTime.UtcNow;
        if (writing)
            return;
        writing = true;
        persistence = Task.Run(PersistLoop);
    }
    private void PersistLoop()
    {
        try
        {
            while (true)
            {
                TransferRecord[] snapshot;
                lock (gate)
                {
                    if (!savePending)
                        return;
                    savePending = false;
                    snapshot = history.Values.ToArray();
                }
                try
                {
                    store.SetHistory(snapshot);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { PersistenceFailed?.Invoke(error); }
            }
        }
        finally { lock (gate) { writing = false; if (savePending) Save(); } }
    }
    public async Task FlushAsync(CancellationToken token = default)
    {
        while (true)
        {
            Task pending;
            lock (gate)
            {
                if (!writing && !savePending)
                    return;
                pending = persistence;
            }
            await pending.WaitAsync(token);
        }
    }
    public void Cancel(string id, string direction)
    {
        TransferJob? job;
        lock (gate)
            jobs.TryGetValue(id + direction, out job);
        job?.Cancel();
    }
    public void Revoke(string fingerprint)
    {
        TransferJob[] selected;
        lock (gate)
            selected = jobs.Values.Where(j => j.Snapshot.PeerFingerprint == fingerprint && !j.Snapshot.Terminal).ToArray();
        foreach (TransferJob job in selected)
            job.Cancel();
    }
    public void CancelAll()
    {
        TransferJob[] all;
        lock (gate)
            all = jobs.Values.ToArray();
        foreach (TransferJob job in all.Where(j => !j.Snapshot.Terminal))
            job.Cancel();
    }
    public void ClearFinished()
    {
        lock (gate)
        {
            foreach (string id in history.Where(p => p.Value.Terminal).Select(p => p.Key).ToArray())
                history.Remove(id);
            Save();
        }
    }
}
