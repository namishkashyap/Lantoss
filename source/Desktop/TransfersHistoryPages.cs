using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lantoss.Desktop;

internal sealed class TransfersPage : IPage
{
    private readonly Shell app;
    private readonly StackPanel rows = new();
    private readonly Dictionary<string, TransferRow> cards = [];
    private readonly Dictionary<string, Button> filters = [];
    private string filter = "All", layoutKey = "";
    public Control View
    {
        get;
    }
    public TransfersPage(Shell app)
    {
        this.app = app;
        StackPanel tabs = C.Inline();
        foreach (string name in new[] { "All", "Active", "Queued", "Completed", "Failed" })
        {
            Button button = C.Small(name, () => { filter = name; layoutKey = ""; Refresh(); });
            filters[name] = button;
            tabs.Children.Add(button);
        }
        View = C.Stack(C.Space(C.Text("Every file. Every step.", 26, bold: true), 9), C.Space(C.Text("Actual transfer progress, with integrity checked before completion.", 13, "Muted"), 22), C.Space(tabs, 20), rows,
            C.Space(C.Text("Retry starts a new transfer and asks the recipient for approval again.", 11, "Muted"), 0, 20));
    }
    public void Refresh()
    {
        foreach ((string name, Button button) in filters)
            Theme.SetButton(button.Resources, name == filter ? "PrimarySoft" : "Surface", "Text", name == filter ? "Primary" : "Border", "PrimarySoft");
        TransferRecord[] records = app.Engine.Transfers.Records.Where(r => Matches(r) && (r.Title.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.PeerName.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.Files.Any(f => f.RelativePath.Contains(app.Search, StringComparison.OrdinalIgnoreCase)))).Take(100).ToArray();
        string key = string.Join('|', records.Select(r => r.Id + r.Direction));
        // Rebuild only when the set of jobs changes; progress updates reuse the existing cards.
        if (layoutKey != key || rows.Children.Count == 0)
        {
            layoutKey = key;
            rows.Children.Clear();
            cards.Clear();
            if (records.Length == 0)
                rows.Children.Add(C.Empty("Nothing in this queue.", "Send files from the Send screen, or approve an incoming transfer. Active and finished jobs appear here.", "transfer", C.AsyncButton("Choose files", app.PickFilesAsync, icon: "plus")));
            foreach (TransferRecord record in records)
            {
                TransferRow row = new(app, record);
                cards[record.Id + record.Direction] = row;
                rows.Children.Add(C.Space(row.View, 14));
            }
        }
        foreach (TransferRecord record in records)
            if (cards.TryGetValue(record.Id + record.Direction, out TransferRow? row))
                row.Update(record);
    }
    private bool Matches(TransferRecord record) => filter switch
    {
        "Active" => !record.Terminal && record.State != TransferState.Queued,
        "Queued" => record.State == TransferState.Queued,
        "Completed" => record.State == TransferState.Completed,
        "Failed" => record.State == TransferState.Failed,
        _ => true
    };
}

internal sealed class TransferRow
{
    private readonly Shell app;
    private TransferRecord record;
    private readonly TextBlock title = C.Text("", 15, bold: true), status = C.Text("", 12, "Primary", true), detail = C.Text("", 11, "Muted"), current = C.Text("", 12), amount = C.Text("", 11, "Muted"), speed = C.Text("", 11, "Primary"), error = C.Text("", 12, "Danger");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6, Margin = new(0, 12) };
    private readonly StackPanel buttons = C.Inline(), fileDetails = new() { IsVisible = false };
    private readonly Button details;
    private bool lastTerminal;
    public Border View
    {
        get;
    }
    public TransferRow(Shell app, TransferRecord record)
    {
        this.app = app;
        this.record = record;
        DockPanel heading = new();
        DockPanel.SetDock(status, Dock.Right);
        heading.Children.Add(status);
        heading.Children.Add(title);
        DockPanel counts = new();
        DockPanel.SetDock(speed, Dock.Right);
        counts.Children.Add(speed);
        counts.Children.Add(amount);
        details = C.Small("File details", () => { fileDetails.IsVisible = !fileDetails.IsVisible; UpdateFiles(); });
        View = C.Card(C.Stack(C.Space(heading, 8), C.Space(detail, 12), current, progress, C.Space(counts, 8), C.Space(error, 10), C.Space(buttons, 12), details, C.Space(fileDetails, 0, 12)));
        UpdateButtons();
        Update(record);
    }
    public void Update(TransferRecord next)
    {
        record = next;
        title.Text = record.FileCount + record.FolderCount > 1 ? $"{Count(record.FileCount, "file")} · {Count(record.FolderCount, "folder")}" : record.Title;
        status.Text = record.StatusText;
        status.Foreground = Theme.Brush(record.State == TransferState.Completed ? "Success" : record.State == TransferState.Failed ? "Danger" : "Primary");
        detail.Text = $"{(record.Direction == "Received" ? "From" : "To")} {record.PeerName} · LAN {record.PeerAddress} · {record.Transport}";
        current.Text = record.CurrentFile.Length > 0 ? record.CurrentFile : record.State == TransferState.AwaitingApproval ? "Waiting for the recipient to choose Accept or Decline." : "Preparing the transfer.";
        if (record.State == TransferState.Completed)
            current.Text = record.Transport.Contains("HTTP", StringComparison.Ordinal) ? record.Direction == "Received" ? "Saved on this computer · SHA-256 recorded locally" : "Sent to browser · saving is controlled by that device" : $"{Count(record.VerifiedFiles, "file")} verified · {Count(record.FolderCount, "folder")} · {Count(record.SkippedFiles, "file")} skipped";
        progress.Value = record.Progress;
        progress.Foreground = Theme.Brush(record.State == TransferState.Completed ? "Success" : "Primary");
        amount.Text = record.ProgressText;
        speed.Text = record.SpeedText + (record.TimeLeft.Length > 0 ? " · " + record.TimeLeft : "");
        error.Text = record.Error;
        error.IsVisible = record.Error.Length > 0;
        details.Content = $"File details ({record.Files.Count})";
        if (fileDetails.IsVisible)
            UpdateFiles();
        if (lastTerminal != record.Terminal)
            UpdateButtons();
        lastTerminal = record.Terminal;
    }
    private static string Count(int value, string singular) => value + " " + singular + (value == 1 ? "" : "s");
    private void UpdateButtons()
    {
        buttons.Children.Clear();
        if (!record.Terminal)
            buttons.Children.Add(C.Small("Cancel", () => app.Engine.Transfers.Cancel(record.Id, record.Direction)));
        if (record.Terminal && record.Direction == "Sent" && record.State != TransferState.Completed && !record.Transport.Contains("HTTP", StringComparison.Ordinal))
            buttons.Children.Add(C.Small("Retry", () => { app.Engine.Retry(record); app.Tell("Retry queued. The recipient will be asked again."); }));
        if (record.Direction == "Received" && Directory.Exists(record.Destination))
            buttons.Children.Add(C.Small("Reveal receive folder", () => app.Reveal(record.Destination)));
    }
    private void UpdateFiles()
    {
        fileDetails.Children.Clear();
        foreach (TransferFile file in record.Files.Take(200))
            fileDetails.Children.Add(C.Space(C.Text($"{file.RelativePath}\n{file.Status} · {Format.Bytes(file.Bytes)} / {Format.Bytes(file.Size)}", 11, "Muted"), 10));
        if (record.Files.Count > 200)
            fileDetails.Children.Add(C.Text("Showing the first 200 file details. The current file is shown above.", 11, "Muted"));
    }
}

internal sealed class HistoryPage : IPage
{
    private readonly Shell app;
    private readonly StackPanel list = new();
    public Control View
    {
        get;
    }
    public HistoryPage(Shell app)
    {
        this.app = app;
        View = C.Stack(C.Space(C.Text("A record of what moved.", 26, bold: true), 9), C.Space(C.Text("Search by filename, person or device. History contains local metadata, never file contents.", 13, "Muted"), 20),
            C.Space(C.Inline(C.AsyncButton("Export log", ExportAsync), C.AsyncButton("Clear finished history", ClearAsync)), 22), list);
    }
    public void Refresh()
    {
        list.Children.Clear();
        TransferRecord[] records = app.Engine.Transfers.Records.Where(r => r.Terminal && (r.Title.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.PeerName.Contains(app.Search, StringComparison.OrdinalIgnoreCase) || r.Files.Any(f => f.RelativePath.Contains(app.Search, StringComparison.OrdinalIgnoreCase)))).Take(100).ToArray();
        if (records.Length == 0)
            list.Children.Add(C.Empty("No matching history.", "Completed, declined, cancelled, and failed transfers appear here. Clearing history keeps received files.", "history"));
        foreach (TransferRecord record in records)
        {
            string color = record.State == TransferState.Completed ? "Success" : record.State == TransferState.Failed ? "Danger" : "Primary";
            StackPanel info = C.Stack(C.Heading(record.Title, C.Badge(record.StatusText, color, color == "Primary" ? "PrimarySoft" : color + "Soft"), 13),
                C.Text($"{(record.Direction == "Received" ? "From" : "To")} {record.PeerName}{(record.PeerAddress.Length > 0 ? " · " + record.PeerAddress : "")} · {record.SizeText} · {record.CreatedUtc.ToLocalTime():dd MMM, HH:mm}", 11, "Muted"));
            if (record.Direction == "Received" && Directory.Exists(record.Destination))
                info.Children.Add(C.Space(C.Small("Reveal receive folder", () => app.Reveal(record.Destination)), 0, 10));
            list.Children.Add(C.Space(C.Card(info, 17), 14));
        }
    }
    private async Task ClearAsync()
    {
        if (!await app.Dialogs.ConfirmAsync("Clear history", "Clear finished transfer metadata? Received files stay on disk. Active transfers remain listed.", "Clear"))
            return;
        app.Engine.Transfers.ClearFinished();
        Refresh();
        app.Tell("Finished history cleared. Received files were kept.");
    }
    private async Task ExportAsync()
    {
        IStorageFile? file = await app.Window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export transfer metadata", SuggestedFileName = "Lantoss-history.csv", DefaultExtension = "csv" });
        if (file == null)
            return;
        StringBuilder csv = new("Time UTC,Direction,Person or device,Status,Files,Bytes,Verified files,Skipped files\r\n");
        foreach (TransferRecord r in app.Engine.Transfers.Records)
            csv.AppendLine(string.Join(',', new[] { r.CreatedUtc.ToString("O"), r.Direction, r.PeerName, r.State.ToString(), r.FileCount.ToString(), r.TotalBytes.ToString(), r.VerifiedFiles.ToString(), r.SkippedFiles.ToString() }.Select(Cell)));
        await using Stream stream = await file.OpenWriteAsync();
        await stream.WriteAsync(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        app.Tell("Exported metadata without file contents, paths, addresses, pairing codes, or keys.");
        // Leading formula characters are neutralized so a spreadsheet never evaluates a device name.
        static string Cell(string value)
        {
            if (value.Length > 0 && "=+-@".Contains(value[0]))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
