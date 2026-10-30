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

    // hopo design tokens. Glass, GlassBorder, Menu, Scrim and Ring carry alpha (#AARRGGBB) and sit over the GlowA/GlowB field;
    // every solid text/background pair keeps 4.5:1. Light mode keeps the amber buttons but uses deep amber for text and icons.
    public static IReadOnlyDictionary<string, string> Palette(bool dark) => dark
        ? new Dictionary<string, string>
        {
            ["Background"] = "#07080C",
            ["Surface"] = "#12141C",
            ["Sidebar"] = "#0B0C12",
            ["Text"] = "#EDEEF2",
            ["Muted"] = "#9BA1AE",
            ["Border"] = "#2A2D38",
            ["Primary"] = "#F5B54A",
            ["PrimarySoft"] = "#241D11",
            ["Accent"] = "#F5B54A",
            ["OnAccent"] = "#1A1204",
            ["Success"] = "#6EE7C5",
            ["SuccessSoft"] = "#10251F",
            ["Danger"] = "#FF8A80",
            ["DangerSoft"] = "#3A1A1A",
            ["Glass"] = "#0EFFFFFF",
            ["GlassBorder"] = "#1CFFFFFF",
            ["Menu"] = "#EB12141C",
            ["Scrim"] = "#99040508",
            ["Ring"] = "#16FFFFFF",
            ["GlowA"] = "#8C3CFF",
            ["GlowB"] = "#28F096",
            ["Mint"] = "#6EE7C5",
            ["Phone"] = "#B57CFF"
        }
        : new Dictionary<string, string>
        {
            ["Background"] = "#F4F5F9",
            ["Surface"] = "#FFFFFF",
            ["Sidebar"] = "#FAFAFC",
            ["Text"] = "#14161D",
            ["Muted"] = "#555C6B",
            ["Border"] = "#DCDFE7",
            ["Primary"] = "#8A5300",
            ["PrimarySoft"] = "#FBEFD9",
            ["Accent"] = "#F5B54A",
            ["OnAccent"] = "#1A1204",
            ["Success"] = "#0B7A5F",
            ["SuccessSoft"] = "#DDF4EC",
            ["Danger"] = "#B42F37",
            ["DangerSoft"] = "#FCE7E6",
            ["Glass"] = "#C2FFFFFF",
            ["GlassBorder"] = "#2414161D",
            ["Menu"] = "#F5FFFFFF",
            ["Scrim"] = "#5914161D",
            ["Ring"] = "#1C14161D",
            ["GlowA"] = "#A98BFF",
            ["GlowB"] = "#5FD9AE",
            ["Mint"] = "#0B7A5F",
            ["Phone"] = "#7B4FD6"
        };
}
