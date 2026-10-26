namespace Lantoss.Core;

/// <summary>Semantic color tokens shared by every native page. Avoid page-specific hex colors.</summary>
public static class Appearance
{
    public static readonly string[] Modes = ["System", "Light", "Dark"];

    // The reader is injected so tests never need to change the user's Windows settings.
    // A missing/blocked Windows preference falls back to Light without changing the saved mode.
    public static bool IsDark(string mode, Func<bool> systemUsesDark)
    {
        if (mode == "Dark")
            return true;
        if (mode == "Light")
            return false;
        if (mode != "System")
            throw new ArgumentException("Choose System, Light, or Dark.", nameof(mode));
        try
        {
            return systemUsesDark();
        }
        catch { return false; }
    }

    public static IReadOnlyDictionary<string, string> Palette(bool dark) => dark
        ? new Dictionary<string, string>
        {
            ["Background"] = "#0C1715",
            ["Surface"] = "#15241F",
            ["Sidebar"] = "#0F1D1A",
            ["Text"] = "#EEF7F2",
            ["Muted"] = "#A9C2B7",
            ["Border"] = "#2E453C",
            ["Primary"] = "#6FE0B3",
            ["PrimarySoft"] = "#1C3C30",
            ["Accent"] = "#6FE0B3",
            ["OnAccent"] = "#0B1F18",
            ["Success"] = "#74E0B4",
            ["SuccessSoft"] = "#1C3C30",
            ["Danger"] = "#FFB4AB",
            ["DangerSoft"] = "#472826"
        }
        : new Dictionary<string, string>
        {
            ["Background"] = "#F1F5F3",
            ["Surface"] = "#FFFFFF",
            ["Sidebar"] = "#E6EEEA",
            ["Text"] = "#0E211D",
            ["Muted"] = "#4A625C",
            ["Border"] = "#CEDCD6",
            ["Primary"] = "#08634F",
            ["PrimarySoft"] = "#D8EFE5",
            ["Accent"] = "#0A6E58",
            ["OnAccent"] = "#FFFFFF",
            ["Success"] = "#0F6B47",
            ["SuccessSoft"] = "#D8EFE5",
            ["Danger"] = "#B42F37",
            ["DangerSoft"] = "#FCE7E6"
        };
}
