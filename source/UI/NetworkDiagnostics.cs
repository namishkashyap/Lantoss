using System.Diagnostics;

namespace Lantoss.UI;

internal static class NetworkDiagnostics
{
    public static void Show(AppShell app)
    {
        Window dialog = new()
        {
            Title = "Connection diagnostics",
            Owner = app.Window,
            Width = 700,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Resources.MergedDictionaries.Add(app.Window.Resources);
        dialog.SetResourceReference(Control.BackgroundProperty, "Background");
        TextBox details = new()
        {
            Text = app.Engine.ConnectionDiagnostics + "\n\n" + app.Firewall.Status,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 190
        };
        Button settings = C.Button("Open Windows network settings", () => Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }), true);
        Button copy = C.Button("Copy diagnostics", () => Clipboard.SetText(details.Text));
        copy.Margin = new(10, 0, 0, 0);
        Button setup = C.AsyncButton("Set up Private-network access", async () => { if (!app.Engine.Running) app.ToggleReceiving(); await app.Firewall.EnsureAsync(true); details.Text = app.Engine.ConnectionDiagnostics + "\n\n" + app.Firewall.Status; }, true);
        StackPanel body = C.Stack(C.Space(C.Text("Make the connection visible.", 23, bold: true), 15), C.Space(details, 18),
            C.Space(C.Text("Automatic setup requests Windows administrator approval for this app's local-subnet access on Private networks. It keeps the firewall enabled and leaves Public/Domain profiles protected. Your trusted home network must be marked Private in Windows. Router guest isolation, VPN routing, and managed policy can still prevent communication.", 12, "Muted"), 18), C.Space(setup, 12), C.Inline(settings, copy));
        body.Margin = new(25);
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.ShowDialog();
    }
}
