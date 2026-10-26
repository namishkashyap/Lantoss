namespace Lantoss.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Preview runs use a memory-only identity and never start normal LAN/firewall setup.
        bool preview = args.Contains("--preview") || args.Contains("--ui-smoke") || args.Contains("--preview-browser") || args.Contains("--ui-regression");
        string Option(string name, string fallback)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }
        string data = Option("--data-dir", preview ? Path.Combine(Environment.CurrentDirectory, "work", "lantoss", "preview-state") : AppDataPaths.DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        // Keep the old mutex namespace: both names must lock the same reused store.
        using Mutex? instance = preview ? null : new Mutex(false, "Local\\FlowShare-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant())))[..20]);
        bool ownsInstance = false;
        try
        {
            if (instance != null)
            {
                try
                {
                    ownsInstance = instance.WaitOne(0);
                }
                catch (AbandonedMutexException) { ownsInstance = true; }
                if (!ownsInstance)
                {
                    MessageBox.Show("Lantoss is already running for this Windows profile. Use its open window or tray icon.", "Lantoss");
                    return 0;
                }
            }
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            Application app = new();
            app.DispatcherUnhandledException += (_, e) => { if (!preview) MessageBox.Show(e.Exception.Message, "Lantoss", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true; };
            MetadataStore store = new(data);
            Identity identity = preview ? Identity.Ephemeral() : Identity.Windows(store);
            using AppShell shell = new(store, identity, preview);
            if (preview)
            {
                if (args.Contains("--ui-regression"))
                {
                    UiRegressionChecks.Run(shell, Option("--ui-regression", Path.Combine(data, "ui-checks")));
                    return 0;
                }
                if (args.Contains("--preview-browser"))
                {
                    NetworkAdapter loopback = new("Local browser UI check", System.Net.IPAddress.Loopback, System.Net.IPAddress.Parse("255.0.0.0"), false);
                    shell.Engine.Browser.StartAsync(loopback, 0).GetAwaiter().GetResult();
                    using System.Net.Http.HttpClient check = new(new System.Net.Http.HttpClientHandler { UseProxy = false });
                    string html = check.GetStringAsync(shell.Engine.Browser.Url + "/").GetAwaiter().GetResult();
                    if (!html.Contains("portal.js", StringComparison.Ordinal))
                        throw new InvalidDataException("The packaged browser page did not load.");
                }
                shell.ApplyTheme(Option("--theme", "Light"));
                if (args.Contains("--ui-smoke"))
                {
                    string output = Option("--ui-smoke", Path.Combine(data, "screens"));
                    Directory.CreateDirectory(output);
                    foreach (string theme in new[] { "Light", "Dark" })
                        foreach (string page in new[] { "Home", "Send", "Transfers", "Devices", "Phone & browser", "Shared Spaces", "Sync", "History", "Settings" })
                        {
                            shell.ApplyTheme(theme);
                            shell.Navigate(page);
                            shell.Render(Path.Combine(output, $"{theme}-{page.Replace(' ', '-')}.png"), theme == "Dark" ? 1000 : 1440, theme == "Dark" ? 720 : 900);
                        }
                }
                else
                {
                    shell.Navigate(Option("--page", "Home"));
                    shell.Render(Option("--preview", Path.Combine(data, "preview.png")), int.Parse(Option("--width", "1440")), int.Parse(Option("--height", "900")));
                }
                return 0;
            }
            app.Run(shell.Window);
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(data, "startup-error.txt"), error.ToString());
            }
            catch { }
            if (!preview)
                MessageBox.Show("Lantoss could not start.\n\n" + error.Message + "\n\nDetails are in the app's startup-error.txt file.", "Lantoss", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally { if (ownsInstance) instance?.ReleaseMutex(); }
    }
}
