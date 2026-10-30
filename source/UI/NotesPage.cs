using System.Diagnostics;

namespace Lantoss.UI;

internal sealed class NotesPage : IPage
{
    private sealed record Destination(string Label, Peer? Peer)
    {
        public override string ToString() => Label;
    }
    private readonly AppShell app;
    private readonly TextBox draft = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = SharedNotes.MaxCharacters };
    private readonly ComboBox target = new() { MinWidth = 220, Height = 40, DisplayMemberPath = "Label" };
    private readonly StackPanel notes = new();
    public FrameworkElement View
    {
        get;
    }
    public NotesPage(AppShell app)
    {
        this.app = app;
        System.Windows.Automation.AutomationProperties.SetName(draft, "Text or link to share");
        Button send = C.AsyncButton("Share text or link", async () =>
        {
            string content = SharedNotes.Validate(draft.Text);
            if (target.SelectedItem is Destination { Peer: { } peer })
                await app.Engine.SendNoteAsync(peer, content);
            else
                app.Engine.Browser.Notes.Add(content, "This PC", local: true);
            if (draft.Text.Trim() == content)
                draft.Clear();
            Refresh();
            app.Tell("Text shared. Find it in Text & links on the receiving PC or its browser page.");
        }, true, "send");
        send.Margin = new(12, 0, 0, 0);
        View = C.Stack(C.Space(C.Text("Text & links", 26, bold: true), 9),
            C.Space(C.Text("Share a note or link with a connected PC, or save it here for your phone. Nothing opens or copies automatically.", 13, "Muted"), 22),
            C.Space(C.Card(C.Stack(C.Space(draft, 14), C.Inline(target, send), C.Space(C.Text("Up to 16,000 characters. Notes saved here are visible to connected browsers; optional access-code protection applies.", 11, "Muted"), 0, 12))), 24),
            C.Heading("Shared on this PC", C.Small("Refresh", Refresh)), notes);
        Refresh();
    }
    public void Refresh()
    {
        // Refresh only the list and recipient names. Incoming notes never replace a typed draft.
        string? selected = (target.SelectedItem as Destination)?.Peer?.Fingerprint;
        Destination[] destinations = [new("This PC · share with my browser", null), .. app.Engine.Peers.Where(p => p.Paired && !p.Blocked).Select(p => new Destination(p.Name, p))];
        target.ItemsSource = destinations;
        target.SelectedItem = destinations.FirstOrDefault(item => item.Peer?.Fingerprint == selected) ?? destinations[0];
        notes.Children.Clear();
        SharedNote[] visible = app.Engine.Browser.Notes.Snapshot().Notes.Where(note => note.Content.Contains(app.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (visible.Length == 0)
            notes.Children.Add(C.Card(C.Text("Shared text and links will appear here.", 13, "Muted")));
        foreach (SharedNote note in visible)
        {
            Button copy = C.Small("Copy", () => { Clipboard.SetText(note.Content); app.Tell("Copied."); });
            Button remove = C.Small("Delete", () => app.Engine.Browser.Notes.Remove(note.Id));
            remove.Margin = new(8, 0, 0, 0);
            StackPanel actions = C.Inline(copy, remove);
            if (note.Kind == "link")
            {
                Button open = C.Small("Open link", () => { if (SharedNote.Link(note.Content) is Uri link && MessageBox.Show(app.Window, SharedNote.OpenPrompt(link, note.Sender), "Open link", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose(); });
                open.Margin = new(8, 0, 0, 0);
                actions.Children.Add(open);
            }
            notes.Children.Add(C.Space(C.Card(C.Stack(C.Space(C.Text(note.Content, 14), 12), C.Space(C.Text(note.Sender + " · " + note.CreatedUtc.ToLocalTime().ToString("g"), 11, "Muted"), 12), actions), 18), 12));
        }
    }
}
