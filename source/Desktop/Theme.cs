using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Lantoss.Desktop;

/// <summary>
/// The same semantic palette as the Windows app (Core/Appearance.cs). Pages read brushes when they are built,
/// so changing the appearance rebuilds the shell and the current page instead of rebinding every control.
/// </summary>
internal static class Theme
{
    private static IReadOnlyDictionary<string, string> palette = Appearance.Palette(false);
    public static bool Dark
    {
        get; private set;
    }
    public static IBrush Brush(string name) => new SolidColorBrush(Avalonia.Media.Color.Parse(palette.TryGetValue(name, out string? value) ? value : palette["Text"]));
    public static Color Color(string name) => Avalonia.Media.Color.Parse(palette[name]);
    public static bool SystemIsDark() => Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
    public static void Apply(string mode)
    {
        Dark = Appearance.IsDark(mode, SystemIsDark);
        palette = Appearance.Palette(Dark);
        Application app = Application.Current!;
        app.RequestedThemeVariant = Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        // Fluent controls (check boxes, text boxes, progress bars, focus rings) follow the Lantoss accent and shape.
        app.Resources["SystemAccentColor"] = Color("Accent");
        app.Resources["SystemAccentColorLight1"] = Color("Primary");
        app.Resources["SystemAccentColorDark1"] = Color("Accent");
        app.Resources["ControlCornerRadius"] = new CornerRadius(10);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(12);
        SetButton(app.Resources, "Surface", "Text", "Border", "PrimarySoft");
    }
    /// <summary>Fluent buttons look up these resources from the nearest element, so a button can carry its own colors for every state.</summary>
    public static void SetButton(IResourceDictionary resources, string background, string foreground, string border, string hover)
    {
        resources["ButtonBackground"] = Brush(background);
        resources["ButtonBackgroundPointerOver"] = Brush(hover);
        resources["ButtonBackgroundPressed"] = Brush(hover);
        resources["ButtonBackgroundDisabled"] = Brush(background);
        resources["ButtonForeground"] = Brush(foreground);
        resources["ButtonForegroundPointerOver"] = Brush(hover == "Primary" ? "OnAccent" : foreground);
        resources["ButtonForegroundPressed"] = Brush(hover == "Primary" ? "OnAccent" : foreground);
        resources["ButtonForegroundDisabled"] = Brush("Muted");
        resources["ButtonBorderBrush"] = Brush(border);
        resources["ButtonBorderBrushPointerOver"] = Brush(hover == "PrimarySoft" ? "Primary" : hover);
        resources["ButtonBorderBrushPressed"] = Brush("Primary");
        resources["ButtonBorderBrushDisabled"] = Brush(border);
    }
}
