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
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new
        {
            passed,
            modes = new[] { "Light 1440x900", "Dark 1000x720" },
            fixture = "Synthetic PREVIEW device NK; no connection, firewall, or keys persisted outside scratch"
        }));
    }
}
