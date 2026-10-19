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
            ["Background"] = "#101B19",
            ["Surface"] = "#192824",
            ["Sidebar"] = "#12211D",
            ["Text"] = "#ECF6F0",
            ["Muted"] = "#ACC3B8",
            ["Border"] = "#344B42",
            ["Primary"] = "#72DDB2",
            ["PrimarySoft"] = "#203F33",
            ["Accent"] = "#72DDB2",
            ["OnAccent"] = "#10251D",
            ["Success"] = "#73DEB2",
            ["SuccessSoft"] = "#203F33",
            ["Danger"] = "#FFB4AB",
            ["DangerSoft"] = "#482A2A"
        }
        : new Dictionary<string, string>
        {
            ["Background"] = "#F3F7F5",
            ["Surface"] = "#FFFFFF",
            ["Sidebar"] = "#EAF1EE",
            ["Text"] = "#142E2A",
            ["Muted"] = "#536D66",
            ["Border"] = "#CFDDD7",
            ["Primary"] = "#0A6B58",
            ["PrimarySoft"] = "#DDF1E9",
            ["Accent"] = "#08745D",
            ["OnAccent"] = "#FFFFFF",
            ["Success"] = "#126B4A",
            ["SuccessSoft"] = "#DDF1E9",
            ["Danger"] = "#AD343B",
            ["DangerSoft"] = "#FCE8E7"
        };
}
