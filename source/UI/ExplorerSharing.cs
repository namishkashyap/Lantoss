using System.Windows.Threading;

namespace Lantoss.UI;

/// <summary>Coalesces per-item Explorer launches and prepares a visible batch for the chosen saved key.</summary>
internal sealed class ExplorerSharing(AppShell app)
{
    private readonly Queue<LaunchRequest> requests = new();
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool working;
    public void Accept(LaunchRequest request)
    {
        request = request.Checked();
        if (request.Kind == "background")
            return;
        app.ShowWindow();
        if (request.Kind == "activate")
            return;
        requests.Enqueue(request);
        if (!working)
        {
            debounce.Stop();
            debounce.Tick -= Tick;
            debounce.Tick += Tick;
            debounce.Start();
        }
    }
    private async void Tick(object? sender, EventArgs e)
    {
        debounce.Stop();
        if (working || requests.Count == 0)
            return;
        working = true;
        try
        {
            // Process each recipient separately rather than accidentally mixing files for different PCs.
            while (requests.Count > 0)
            {
                LaunchRequest first = requests.Dequeue();
                List<string> paths = [.. first.Paths];
                while (requests.TryPeek(out LaunchRequest? next) && next.PeerKey == first.PeerKey)
                    paths.AddRange(requests.Dequeue().Paths);
                await app.PrepareExplorerShareAsync(paths, first.PeerKey);
                if (requests.Count > 0)
                    app.Tell("Another Explorer request is being prepared.");
            }
        }
        catch (Exception error) { app.Error(error); }
        finally
        {
            working = false;
            if (requests.Count > 0)
                debounce.Start();
        }
    }
}
