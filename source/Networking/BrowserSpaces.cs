using System.Collections.Concurrent;
using Lantoss.Core;
using Microsoft.AspNetCore.Http;

// Browser shared-folder routes reuse the same guarded catalog as native operations. Never accept a browser-supplied absolute destination path.
namespace Lantoss.Networking;

public sealed partial class BrowserPortal
{
    private sealed record BrowserJob(string Session, DateTime? StartedUtc);
    private readonly Dictionary<string, BrowserJob> browserJobs = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> spaceTokens = new();
    public SpaceCatalog Spaces
    {
        get;
    }
    private CancellationToken SpaceToken(string id) => spaceTokens.GetOrAdd(id, _ => new()).Token;
    private void RevokeSpace(string id)
    {
        if (spaceTokens.TryRemove(id, out CancellationTokenSource? token))
            token.Cancel();
        foreach (Offer offer in offers.Values.Where(offer => offer.SpaceId == id))
        {
            offer.Cancellation.Cancel();
            offer.Job.Cancel();
            RemoveOffer(offer);
        }
        Changed?.Invoke();
    }
    private void TrackTransfer(TransferJob job, string session)
    {
        lock (gate)
        {
            if (browserJobs.Count >= 512)
                foreach (string old in browserJobs.Keys.Take(browserJobs.Count - 511).ToArray())
                    browserJobs.Remove(old);
            browserJobs[job.Snapshot.Id] = new(session, null);
        }
    }
    private void StartTransfer(string id)
    {
        lock (gate)
            if (browserJobs.TryGetValue(id, out BrowserJob? job) && job.StartedUtc == null)
                browserJobs[id] = job with
                {
                    StartedUtc = DateTime.UtcNow
                };
    }
    private object[] TransferViews(string session)
    {
        Dictionary<string, BrowserJob> visible;
        lock (gate)
            visible = browserJobs.Where(pair => pair.Value.Session == session).ToDictionary(pair => pair.Key, pair => pair.Value);
        return transfers.Records.Where(record => visible.ContainsKey(record.Id)).Take(12).Select(record =>
        {
            DateTime? started = visible[record.Id].StartedUtc;
            double speed = record.Terminal && started != null ? record.TransferredBytes / Math.Max(.001, (record.UpdatedUtc - started.Value).TotalSeconds) : record.BytesPerSecond;
            double? remaining = !record.Terminal && speed > 0 ? Math.Max(0, (record.TotalBytes - record.TransferredBytes) / speed) : null;
            return (object)new
            {
                id = record.Id,
                title = record.Title,
                direction = record.Direction,
                state = record.State.ToString(),
                bytes = record.TransferredBytes,
                total = record.TotalBytes,
                bytesPerSecond = speed,
                remainingSeconds = remaining,
                average = record.Terminal,
                error = record.State == TransferState.Failed ? "The transfer failed. Check the host PC." : record.Error
            };
        }).ToArray();
    }
    private async Task DownloadSpaceAsync(HttpContext context, string id, string relative, Session session)
    {
        SharedSpace space = Spaces.Get(id);
        string path = Spaces.Resolve(id, relative, false);
        FileInfo file = new(path);
        await DownloadFileAsync(context, new BrowserFile("", file.Name, file.Length, path, file.LastWriteTimeUtc), session, space);
    }
}
