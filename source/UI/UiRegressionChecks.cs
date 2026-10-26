using System.Text.Json;

namespace Lantoss.UI;

internal static class UiRegressionChecks
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (T next in Descendants<T>(child))
                yield return next;
        }
    }
    public static void Run(AppShell app, string output)
    {
        if (!app.Preview)
            throw new InvalidOperationException("UI regression checks require preview mode.");
        Directory.CreateDirectory(output);
        Peer fixture = new()
        {
            Name = "NK",
            Fingerprint = new string('A', 64),
            Address = "192.168.1.181",
            Port = 48585,
            Paired = true,
            KeyVerified = true,
            LastSeenUtc = DateTime.MinValue
        };
        app.Store.UpsertPeer(fixture);
        int passed = 0;
        foreach (string theme in new[] { "Light", "Dark" })
        {
            app.ApplyTheme(theme);
            app.Navigate("Shared Spaces");
            app.Render(Path.Combine(output, theme + "-Shared-Spaces-NK.png"), theme == "Dark" ? 1000 : 1440, theme == "Dark" ? 720 : 900);
            ComboBox device = Descendants<ComboBox>(app.Window.Content as DependencyObject ?? app.Window).Single(control => control.SelectedItem is Peer);
            string[] labels = Descendants<TextBlock>(device).Select(text => text.Text).ToArray();
            if (!labels.Contains("NK") || labels.Any(text => text.Contains("Fingerprint", StringComparison.Ordinal) || text.Contains("Peer {", StringComparison.Ordinal)))
                throw new InvalidDataException("A selected PC must display its name without the internal peer record.");
            passed++;
            if (device.ActualHeight > 42 || device.ItemTemplate == null)
                throw new InvalidDataException("The connected-PC selector must stay compact and use its explicit name template.");
            passed++;
            Button refresh = Descendants<Button>(app.Window.Content as DependencyObject ?? app.Window).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Refresh folders");
            if (refresh.ActualHeight > 45)
                throw new InvalidDataException("The refresh button stretched with the device record.");
            passed++;
            app.SelectedPeerKey = fixture.Fingerprint;
            app.Navigate("Send");
            app.Render(Path.Combine(output, theme + "-Send-NK.png"), theme == "Dark" ? 1000 : 1440, theme == "Dark" ? 720 : 900);
            ComboBox recipient = Descendants<ComboBox>(app.Window.Content as DependencyObject ?? app.Window).Single(control => control.SelectedItem is Peer);
            if (!Descendants<TextBlock>(recipient).Any(text => text.Text == "NK"))
                throw new InvalidDataException("The Send recipient must also honor the device-name display binding.");
            passed++;
        }
        // Exercise the actual sidebar control and resource updates without rebuilding the page.
        app.Navigate("Home");
        if (app.Window.Title != "Lantoss")
            throw new InvalidDataException("The window must use the new brand.");
        passed++;
        FrameworkElement home = (FrameworkElement)app.Find<ContentControl>("PageContent").Content;
        ComboBox appearance = app.Find<ComboBox>("AppearanceSelector");
        app.Render(Path.Combine(output, "Home-before-switch.png"), 1440, 960);
        Button primary = Descendants<Button>(home).First(button => button.Style == app.Window.FindResource("PrimaryButton"));
        foreach (string mode in new[] { "Light", "Dark", "System" })
        {
            appearance.SelectedItem = mode;
            app.Render(Path.Combine(output, mode + "-Home.png"), 1440, 960);
            if (app.Store.Preferences.Theme != mode || new MetadataStore(app.Store.Root).Preferences.Theme != mode)
                throw new InvalidDataException("Changing appearance must save immediately.");
            passed++;
            if (!ReferenceEquals(home, app.Find<ContentControl>("PageContent").Content))
                throw new InvalidDataException("Appearance must not recreate the active page or lose its state.");
            if (mode == "System" && !app.Find<TextBlock>("AppearanceStatus").Text.StartsWith("Following Windows", StringComparison.Ordinal))
                throw new InvalidDataException("System appearance needs visible Windows-following feedback.");
            passed++;
            Color expected = ((SolidColorBrush)app.Window.FindResource("OnAccent")).Color;
            if (!Descendants<TextBlock>(primary).Any(text => ((SolidColorBrush)text.Foreground).Color == expected))
                throw new InvalidDataException("Primary button labels must update contrast in place.");
            passed++;
        }
        // Exercise Explorer's recipient-picker path without registering shell keys or starting a listener.
        string explorerFile = Path.Combine(app.Store.Root, "Explorer photo & family 😀.txt");
        File.WriteAllText(explorerFile, "Preview-only Explorer selection fixture.");
        // Offscreen previews do not call Application.Run, which normally installs this UI context.
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Window.Dispatcher));
        Task prepare = app.PrepareExplorerShareAsync([explorerFile], null);
        System.Windows.Threading.DispatcherFrame frame = new();
        System.Windows.Threading.DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        timeout.Tick += (_, _) => frame.Continue = false;
        _ = prepare.ContinueWith(_ => app.Window.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        timeout.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timeout.Stop();
        SynchronizationContext.SetSynchronizationContext(previousContext);
        if (!prepare.IsCompleted)
            throw new TimeoutException("Explorer selection did not finish.");
        prepare.GetAwaiter().GetResult();
        if (app.CurrentPage != "Send" || app.Selection.Count != 1 || app.Selection[0].Path != explorerFile)
            throw new InvalidDataException("Explorer must prepare exactly the selected local file in the Send page.");
        passed++;
        app.Render(Path.Combine(output, "Explorer-selection.png"), 1440, 960);
        if (app.Engine.Transfers.Records.Length != 0)
            throw new InvalidDataException("Choose a PC must not send before a recipient is chosen.");
        passed++;
        app.Navigate("Text & links");
        app.Render(Path.Combine(output, "Notes-before-incoming.png"), 1440, 900);
        DependencyObject noteRoot = app.Window.Content as DependencyObject ?? app.Window;
        TextBox noteDraft = Descendants<TextBox>(noteRoot).Single(text => System.Windows.Automation.AutomationProperties.GetName(text) == "Text or link to share");
        noteDraft.Text = "Keep my unfinished note 😀";
        app.Engine.Browser.Notes.Add("<img src=x onerror=alert(1)>\nReceived as plain text.", "Family PC");
        app.Engine.Browser.Notes.Add("https://example.com/family", "Phone / browser");
        app.Engine.Browser.Notes.Add("javascript:alert(1)", "Family PC");
        // Offscreen checks have no Application.Run loop; process the same queued refreshes as a live window.
        app.Window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (noteDraft.Text != "Keep my unfinished note 😀")
            throw new InvalidDataException("Incoming notes must preserve a typed draft.");
        passed++;
        foreach (string mode in new[] { "Light", "Dark" })
        {
            app.ApplyTheme(mode);
            app.Render(Path.Combine(output, mode + "-Text-and-links.png"), mode == "Light" ? 1440 : 1000, mode == "Light" ? 900 : 720);
            if (!Descendants<TextBlock>(noteRoot).Any(text => text.Text.Contains("<img src=x", StringComparison.Ordinal)))
                throw new InvalidDataException("Received markup must render as literal text.");
            passed++;
            if (Descendants<Button>(noteRoot).Count(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Open link") != 1)
                throw new InvalidDataException("Only HTTP/HTTPS notes expose an explicit Open link action.");
            passed++;
        }
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new
        {
            passed,
            modes = new[] { "Light 1440x900", "Dark 1000x720" },
            fixture = "Synthetic PREVIEW device NK; no connection, firewall, or keys persisted outside scratch"
        }));
    }
}
