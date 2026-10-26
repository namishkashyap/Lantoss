using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Lantoss.Desktop;

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? [];
            bool render = args.Contains("--render"), preview = render || args.Contains("--preview");
            string Option(string name, string fallback) => Array.IndexOf(args, name) is int index and >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            try
            {
                string data = Option("--data-dir", preview ? Path.Combine(Environment.CurrentDirectory, "work", "lantoss", "desktop-preview") : AppDataPaths.DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
                Directory.CreateDirectory(data);
                MetadataStore store = new(data);
                Identity identity;
                if (preview)
                    identity = Identity.Ephemeral();
                else if (OperatingSystem.IsWindows())
                    identity = Identity.Windows(store);
                else
                    identity = Identity.Portable(store);
                if (render && store.Peers.Length == 0)
                    store.UpsertPeer(new Peer { Fingerprint = new string('A', 64), Name = "Studio PC", Address = "192.168.1.20", Port = 48585, Paired = true, SupportsDirectConnect = true });
                Shell shell = new(store, identity, preview);
                desktop.MainWindow = shell.Window;
                desktop.ShutdownRequested += (_, e) => e.Cancel = shell.RequestShutdown();
                if (render)
                    shell.Window.Opened += async (_, _) => { await shell.RenderAsync(Option("--render", Path.Combine(data, "screens"))); desktop.Shutdown(); };
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                Theme.Apply("System");
                Window window = new()
                {
                    Title = "Lantoss could not start",
                    Width = 520,
                    SizeToContent = SizeToContent.Height,
                    Background = Theme.Brush("Background")
                };
                window.Content = new Border { Padding = new(28), Child = C.Stack(C.Space(C.Text("Lantoss could not start", 20, bold: true), 12), C.Space(C.Text(error.Message, 13), 20), C.Button("Close", window.Close, true)) };
                desktop.MainWindow = window;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
