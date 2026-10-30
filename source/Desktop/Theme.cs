using System.Globalization;
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
    public static IBrush Brush(string name) => new SolidColorBrush(Color(name));
    /// <summary>A token name, a #hex literal, or "Token/12" for the token at 12% of its opacity, so tints follow the theme without new palette entries.</summary>
    public static Color Color(string name)
    {
        string[] parts = name.Split('/');
        Color color = Avalonia.Media.Color.Parse(parts[0].StartsWith('#') ? parts[0] : palette.TryGetValue(parts[0], out string? value) ? value : palette["Text"]);
        return parts.Length < 2 ? color : new((byte)Math.Round(color.A * double.Parse(parts[1], CultureInfo.InvariantCulture) / 100), color.R, color.G, color.B);
    }
    private static string Percent(double value) => Math.Round(value * 100, 1).ToString(CultureInfo.InvariantCulture);
    /// <summary>One soft radial glow of the hopo field; center and radii are relative to the window.</summary>
    public static IBrush Glow(string name, double strength, double x, double y, double radiusX, double radiusY) => new RadialGradientBrush
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(radiusX, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(radiusY, RelativeUnit.Relative),
        GradientStops = [new(Color(name + "/" + Percent(strength)), 0), new(Color(name + "/" + Percent(strength / 3)), 0.55), new(Color(name + "/0"), 1)]
    };
    /// <summary>Drop shadow for floating glass (menu, dock, device cards); lighter in the light appearance.</summary>
    public static BoxShadows Shadow(double y, double blur, double opacity) => new(new BoxShadow { OffsetY = y, Blur = blur, Color = Color("#000000/" + Percent(Dark ? opacity : opacity / 4)) });
    public static bool SystemIsDark() => Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
    public static void Apply(string mode)
    {
        Dark = Appearance.IsDark(mode, SystemIsDark);
        palette = Appearance.Palette(Dark);
        Application app = Application.Current!;
        app.RequestedThemeVariant = Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        // Fluent controls (check boxes, text boxes, combo boxes, focus rings) follow the hopo accent, glass and shape.
        app.Resources["SystemAccentColor"] = Color("Accent");
        app.Resources["SystemAccentColorLight1"] = Color("Accent");
        app.Resources["SystemAccentColorDark1"] = Color("Primary");
        app.Resources["ControlCornerRadius"] = new CornerRadius(10);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(14);
        foreach (string state in new[] { "", "PointerOver", "Pressed" })
        {
            app.Resources["CheckBoxCheckBackgroundFillChecked" + state] = app.Resources["CheckBoxCheckBackgroundStrokeChecked" + state] = Brush(state == "PointerOver" ? "#FFC567" : "Accent");
            app.Resources["CheckBoxCheckGlyphForegroundChecked" + state] = Brush("OnAccent");
        }
        app.Resources["TextControlBackground"] = app.Resources["TextControlBackgroundPointerOver"] = app.Resources["ComboBoxBackground"] = Brush("Glass");
        app.Resources["TextControlBackgroundFocused"] = Brush("Surface");
        app.Resources["TextControlBorderBrush"] = app.Resources["ComboBoxBorderBrush"] = Brush("GlassBorder");
        app.Resources["TextControlBorderBrushPointerOver"] = app.Resources["ComboBoxBorderBrushPointerOver"] = Brush("Text/22");
        app.Resources["TextControlBorderBrushFocused"] = Brush("Accent");
        app.Resources["ComboBoxBackgroundPointerOver"] = app.Resources["ComboBoxBackgroundPressed"] = Brush("Text/8");
        app.Resources["ComboBoxDropDownBackground"] = Brush("Surface");
        SetButton(app.Resources, "Glass", "Text", "GlassBorder", "Text/8");
    }
    /// <summary>Fluent buttons look up these resources from the nearest element, so a button can carry its own colors for every state.</summary>
    public static void SetButton(IResourceDictionary resources, string background, string foreground, string border, string hover)
    {
        resources["ButtonBackground"] = Brush(background);
        resources["ButtonBackgroundPointerOver"] = resources["ButtonBackgroundPressed"] = Brush(hover);
        resources["ButtonBackgroundDisabled"] = Brush(background == "Accent" ? "Accent/45" : background);
        resources["ButtonForeground"] = resources["ButtonForegroundPointerOver"] = resources["ButtonForegroundPressed"] = Brush(foreground);
        resources["ButtonForegroundDisabled"] = Brush(background == "Accent" ? "OnAccent/60" : "Muted");
        resources["ButtonBorderBrush"] = resources["ButtonBorderBrushDisabled"] = Brush(border);
        resources["ButtonBorderBrushPointerOver"] = Brush(border == "Accent" ? "#FFC567" : border.StartsWith('#') ? border : "Text/22");
        resources["ButtonBorderBrushPressed"] = Brush(border.StartsWith('#') ? border : "Accent");
    }
}
