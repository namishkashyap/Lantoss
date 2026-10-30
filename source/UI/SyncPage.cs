namespace Lantoss.UI;

internal sealed class SyncPage : IPage
{
    private readonly AppShell app;
    private readonly StackPanel links = new();
    public FrameworkElement View
    {
        get;
    }
    public SyncPage(AppShell app)
    {
        this.app = app;
        View = C.Stack(C.Hero("Manage", "Sync", "Keep a folder identical on two PCs. Changes update in the background while both PCs keep hopo open."),
            C.Space(C.Button("Choose another PC's shared folder", () => app.Navigate("Shared Spaces"), true, "folder"), 22), links,
            C.Space(C.Note("Conflicts and earlier versions are kept", "New and changed files sync both ways. Different same-name files retain a conflict copy on both PCs. Replaced versions are kept in the destination's .flowshare-versions folder. Deletions stay local and the remaining copy can restore them. Pause or remove a link to keep a deletion local. Backups are not pruned automatically."), 0, 18));
    }
    public void Refresh()
    {
        links.Children.Clear();
        foreach (SyncLink link in app.Engine.Sync.Headers)
        {
            SyncStatus? status = app.Engine.SyncStatuses.FirstOrDefault(item => item.Id == link.Id);
            Button toggle = C.Button(link.Enabled ? "Pause sync" : "Resume sync", () => { app.Engine.PauseSync(link.Id, link.Enabled); Refresh(); });
            Button now = C.AsyncButton("Sync now", () => app.Engine.SyncOnceAsync(link.Id), true, "sync");
            now.IsEnabled = link.Enabled;
            now.Margin = new(8, 0, 0, 0);
            Button open = C.Small("Open local folder", () => app.Reveal(link.Folder));
            open.Margin = new(8, 0, 0, 0);
            Button remove = C.Small("Remove link", () => { if (MessageBox.Show(app.Window, "Stop automatic sync for this folder? Local files and backups stay on both PCs. The host can revoke its permission under Sync.", "Remove sync link", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) { app.Engine.RemoveSync(link.Id); Refresh(); } });
            remove.Margin = new(8, 0, 0, 0);
            links.Children.Add(C.Space(C.Card(C.Stack(C.Heading(link.Name, C.Badge(link.Enabled ? "Two-way sync" : "Paused")),
                C.Space(C.Text(link.Folder, 11, "Muted"), 10), C.Space(C.Text(status?.Message ?? "Waiting for connection", 12, "Primary"), 9),
                C.Space(C.Text(link.LastSuccessUtc is DateTime last ? "Last successful update: " + last.ToLocalTime().ToString("g") : "First sync pending", 11, "Muted"), 16), C.Inline(toggle, now, open, remove))), 17));
        }
        if (app.Engine.Sync.Headers.Length == 0)
            links.Children.Add(C.Space(C.Empty("No linked folders yet.", "In Shared Spaces, browse another PC and choose Set up automatic sync. On that PC, enable Read & upload for the space, then approve the sync request once.", "sync"), 17));
        SyncGrant[] grants = app.Engine.Sync.Grants;
        if (grants.Length > 0)
            links.Children.Add(C.Space(C.Text("Other PCs allowed to sync folders on this PC", 17, bold: true), 16));
        foreach (SyncGrant grant in grants)
        {
            string peer = app.Store.Peers.FirstOrDefault(item => item.Fingerprint == grant.PeerKey)?.Name ?? "Disconnected PC";
            string space = app.Store.Spaces.FirstOrDefault(item => item.Id == grant.SpaceId)?.Name ?? "Removed space";
            Button revoke = C.Small("Revoke sync access", () => { app.Engine.Sync.Revoke(grant.Id, grant.PeerKey); Refresh(); app.Tell("Automatic writes from this link are no longer allowed. Existing files stay."); });
            links.Children.Add(C.Space(C.Card(C.Heading(peer + " → " + space, revoke, 13)), 12));
        }
    }
}
