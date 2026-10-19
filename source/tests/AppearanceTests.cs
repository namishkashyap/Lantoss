using Lantoss.Core;

internal static partial class Program
{
    private static void AppearanceCases()
    {
        bool windowsDark = false;
        Check(!Appearance.IsDark("System", () => windowsDark), "System appearance initially follows Windows Light");
        windowsDark = true;
        Check(Appearance.IsDark("System", () => windowsDark), "System appearance resolves Windows changes without changing the saved choice");
        Check(Appearance.IsDark("Dark", () => throw new Exception()) && !Appearance.IsDark("Light", () => throw new Exception()), "Explicit appearances override Windows without reading its setting");
        Check(!Appearance.IsDark("System", () => throw new UnauthorizedAccessException()), "An unavailable Windows preference falls back safely to Light");
        foreach (bool dark in new[] { false, true })
        {
            var colors = Appearance.Palette(dark);
            Check(new[] { "Background", "Surface", "Sidebar", "PrimarySoft" }.All(background => Contrast(colors["Text"], colors[background]) >= 4.5 && Contrast(colors["Muted"], colors[background]) >= 4.5)
                && Contrast(colors["Primary"], colors["Surface"]) >= 4.5 && Contrast(colors["OnAccent"], colors["Accent"]) >= 4.5,
                (dark ? "Dark" : "Light") + " palette text, secondary text and action labels meet 4.5:1 contrast");
        }
        MetadataStore settings = Store("Appearance preferences");
        settings.SetPreferences(settings.Preferences with
        {
            Theme = "Dark",
            AutoConnect = false
        });
        Check(new MetadataStore(settings.Root).Preferences is { Theme: "Dark", AutoConnect: false }, "Appearance persists alongside existing connection preferences");
        string profile = Path.Combine(root, "rename-profile");
        string legacy = Path.Combine(profile, "FlowShare");
        string current = Path.Combine(profile, "Lantoss");
        Check(AppDataPaths.DefaultRoot(profile) == current, "A fresh installation uses the Lantoss data folder");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "metadata.json.bak"), "original recovery data");
        File.WriteAllText(Path.Combine(legacy, "sync-state.json"), "original sync data");
        Check(AppDataPaths.DefaultRoot(profile) == legacy && File.ReadAllText(Path.Combine(legacy, "sync-state.json")) == "original sync data" && !Directory.Exists(current), "An upgrade reuses even a recovery-only legacy store without moving or discarding sync data");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(current, "metadata.json"), "existing new installation");
        Check(AppDataPaths.DefaultRoot(profile) == current && File.ReadAllText(Path.Combine(legacy, "metadata.json.bak")) == "original recovery data", "An existing Lantoss store takes priority while preserving legacy data");
    }
    private static double Contrast(string foreground, string background)
    {
        static double Luminance(string color)
        {
            double Channel(int offset)
            {
                double value = Convert.ToInt32(color.Substring(offset, 2), 16) / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
        double a = Luminance(foreground), b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
