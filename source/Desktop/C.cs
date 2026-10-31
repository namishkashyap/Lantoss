using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lantoss.Desktop;

/// <summary>Small Avalonia control factories mirroring the Windows app's helpers, so both apps share the hopo visual language.</summary>
internal static class C
{
    public const string Clear = "#00000000";
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = Theme.Brush(color),
        TextWrapping = TextWrapping.Wrap
    };
    public static TextBlock Medium(string text, double size = 14, string color = "Text")
    {
        TextBlock label = Text(text, size, color);
        label.FontWeight = FontWeight.Medium;
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        return label;
    }
    /// <summary>The small uppercase group label (SHARE, MANAGE) above page titles and menu groups.</summary>
    public static TextBlock Eyebrow(string text)
    {
        TextBlock label = Text(text.ToUpperInvariant(), 11, "Muted", true);
        label.LetterSpacing = 0.9;
        return label;
    }
    /// <summary>Every page except Home opens with an eyebrow, a 24 px title and a one-line muted subtitle; actions sit on the right.</summary>
    public static DockPanel Header(string eyebrow, string title, string subtitle, Control? right = null)
    {
        TextBlock heading = Text(title, 24, bold: true), detail = Text(subtitle, 13, "Muted");
        heading.LetterSpacing = -0.4;
        detail.MaxWidth = 640;
        detail.HorizontalAlignment = HorizontalAlignment.Left;
        DockPanel row = new()
        {
            Margin = new(0, 0, 0, 28)
        };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            right.Margin = new(20, 0, 0, 0);
            row.Children.Add(right);
        }
        row.Children.Add(Stack(Eyebrow(eyebrow), Space(heading, 0, 6), Space(detail, 0, 8)));
        return row;
    }
    public static StackPanel Stack(params Control[] children)
    {
        StackPanel panel = new();
        panel.Children.AddRange(children);
        return panel;
    }
    public static StackPanel Inline(params Control[] children)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        panel.Children.AddRange(children);
        return panel;
    }
    public static WrapPanel Wrap(params Control[] children)
    {
        WrapPanel panel = new()
        {
            Orientation = Orientation.Horizontal
        };
        foreach (Control child in children)
        {
            child.Margin = new(0, 0, 8, 8);
            panel.Children.Add(child);
        }
        return panel;
    }
    public static T Space<T>(T control, double bottom = 16, double top = 0) where T : Control
    {
        control.Margin = new(control.Margin.Left, top, control.Margin.Right, bottom);
        return control;
    }
    public static Border Card(Control child, double padding = 22) => Card(child, new Thickness(padding));
    /// <summary>
    /// Glass card: translucent over the glows, a hairline border and 20 px corners. Its edge catches a light from above and a diagonal
    /// light sweeps along it every 9 s; a purple-to-mint line glows on the top edge. Set padding here: the returned border holds the layers.
    /// </summary>
    public static Border Card(Control child, Thickness padding, double radius = 20)
    {
        RelativePoint At(double x, double y) => new(x, y, RelativeUnit.Relative);
        LinearGradientBrush edge = new()
        {
            StartPoint = At(0, 0),
            EndPoint = At(0, 1),
            GradientStops = [new(Theme.Color("#FFFFFF/30"), 0), new(Theme.Color("#FFFFFF/5"), .35), new(Colors.Transparent, .6)]
        };
        LinearGradientBrush sweep = new()
        {
            StartPoint = At(0, 0),
            EndPoint = At(3, 1.4),
            GradientStops = [new(Colors.Transparent, .35), new(Theme.Color("#FFFFFF/55"), .48), new(Colors.Transparent, .6)]
        };
        LinearGradientBrush sheen = new()
        {
            StartPoint = At(0, 0),
            EndPoint = At(1, 0),
            GradientStops = [new(Theme.Color("#C896FF/0"), .18), new(Theme.Color("#C896FF/95"), .39), new(Theme.Color("#6EFFBE/80"), .61), new(Theme.Color("#6EFFBE/0"), .82)]
        };
        Border Ring(IBrush brush) => new()
        {
            BorderBrush = brush,
            BorderThickness = new(1),
            CornerRadius = new(radius),
            Margin = new(-1),
            IsHitTestVisible = false
        };
        // The line's glow is a blurred copy of the same brush, so both slide together.
        Panel line = new()
        {
            Height = 7,
            Margin = new(-1, -4, -1, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Children = { new Border { Height = 3, Background = sheen, Opacity = .7, Effect = new BlurEffect { Radius = 8 } }, new Border { Height = 1, Background = sheen } }
        };
        Border card = new()
        {
            Child = new Panel { Children = { new Border { Padding = padding, Child = child }, Ring(edge), Ring(sweep), line } },
            Background = Theme.Brush("Glass"),
            BorderBrush = Theme.Brush("GlassBorder"),
            BorderThickness = new(1),
            CornerRadius = new(radius)
        };
        Motion.Card(card, sweep, sheen, line);
        return card;
    }
    public static DockPanel Heading(string title, Control? right = null, double size = 16)
    {
        DockPanel panel = new()
        {
            Margin = new(0, 0, 0, 16)
        };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            right.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(right);
        }
        TextBlock text = Text(title, size, bold: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(text);
        return panel;
    }
    public static Button Button(string text, Action action, bool primary = false, string? icon = null)
    {
        Button button = new()
        {
            Padding = new(18, 0),
            FontSize = 13,
            FontWeight = primary ? FontWeight.SemiBold : FontWeight.Medium,
            MinHeight = 44,
            CornerRadius = new(12),
            BorderThickness = new(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (primary)
        {
            Theme.SetButton(button.Resources, "Accent", "OnAccent", "Accent", "#FFC567");
            button.Classes.Add("primary");
        }
        button.Content = icon == null ? text : Inline(Icon(icon, 16, primary ? "OnAccent" : "Text"), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception error) { Shell.Current.Error(error); }
        };
        return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false, string? icon = null)
    {
        Button button = null!;
        button = Button(text, async () =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception error) { Shell.Current.Error(error); }
            finally { button.IsEnabled = true; }
        }, primary, icon);
        return button;
    }
    private static Button Compact(Button button)
    {
        button.Padding = new(12, 0);
        button.MinHeight = 34;
        button.CornerRadius = new(10);
        button.FontSize = 12;
        return button;
    }
    public static Button Small(string text, Action action, bool primary = false) => Compact(Button(text, action, primary));
    /// <summary>Async lambdas bind here rather than to Action, so their exceptions are reported instead of crashing the app.</summary>
    public static Button Small(string text, Func<Task> action) => Compact(AsyncButton(text, action));
    /// <summary>Borderless button for low-emphasis actions (Clear, Remove).</summary>
    public static T Ghost<T>(T button, string color = "Muted") where T : Button
    {
        Theme.SetButton(button.Resources, Clear, color, Clear, "Text/6");
        return button;
    }
    /// <summary>Text-styled action, like the design's amber links.</summary>
    public static Button Link(string text, Func<Task> action)
    {
        Button button = AsyncButton(text, action);
        button.Padding = new(4, 0);
        button.MinHeight = 28;
        button.FontWeight = FontWeight.Normal;
        Theme.SetButton(button.Resources, Clear, "Primary", Clear, Clear);
        return button;
    }
    /// <summary>Square 44 px icon button (menu, close, remove) with an accessible name.</summary>
    public static Button IconButton(string icon, Action action, string label, bool ghost = false)
    {
        Button button = Button("", action);
        button.Content = Icon(icon, 18, ghost ? "Muted" : "Text");
        button.Width = button.Height = 44;
        button.Padding = new(0);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return ghost ? Ghost(button) : button;
    }
    /// <summary>Rounded chip, 24 px high.</summary>
    public static Border Badge(string text, string color = "Primary", string background = "PrimarySoft")
    {
        TextBlock label = Text(text, 11, color, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;
        return new()
        {
            Child = label,
            Background = Theme.Brush(background),
            CornerRadius = new(8),
            Padding = new(10, 0),
            MinHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
    /// <summary>Check box with a square, slightly rounded box (the shared control radius would make it round).</summary>
    public static CheckBox Check(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value,
        CornerRadius = new(5),
        Margin = new(0, 3)
    };
    public static Ellipse Dot(string color, double size = 8) => new()
    {
        Width = size,
        Height = size,
        Fill = Theme.Brush(color),
        VerticalAlignment = VerticalAlignment.Center
    };
    /// <summary>Rounded icon tile used in lists and the menu; tinted tiles carry the accent.</summary>
    public static Border Tile(string icon, string color = "Text", double size = 40, string? tint = null) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new(10),
        Background = Theme.Brush(tint == null ? "Text/5" : tint + "/12"),
        BorderBrush = Theme.Brush(tint == null ? "Text/8" : tint + "/30"),
        BorderThickness = new(1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Icon(icon, 18, color)
    };
    /// <summary>Dashed drop area.</summary>
    public static Grid Dashed(Control child)
    {
        Grid grid = new();
        grid.Children.Add(new Rectangle { Stroke = Theme.Brush("Text/16"), StrokeThickness = 1, StrokeDashArray = [5, 4], RadiusX = 14, RadiusY = 14 });
        grid.Children.Add(new Border { Padding = new(22), Child = child });
        return grid;
    }
    /// <summary>The phone-page address as people type it: no scheme and no trailing slash.</summary>
    public static string Bare(string url) => (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url[8..] : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? url[7..] : url).TrimEnd('/');
    /// <summary>The design's faint concentric rings, 384 to 1024 px every 64 px, centered on (0, 0) of the returned canvas: fading in to 400 px, then out.</summary>
    public static Canvas Rings(string color = "Ring")
    {
        Canvas canvas = new()
        {
            IsHitTestVisible = false
        };
        for (double radius = 384; radius <= 1024; radius += 64)
        {
            Ellipse ring = new()
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = Theme.Brush(color),
                StrokeThickness = 1,
                Opacity = radius < 400 ? (radius - 340) / 60 : 1 - (radius - 400) / 650
            };
            Canvas.SetLeft(ring, -radius);
            Canvas.SetTop(ring, -radius);
            canvas.Children.Add(ring);
        }
        return canvas;
    }
    /// <summary>The hopo mark: an amber center, two thin rings and a mint dot for a nearby device.</summary>
    public static Control Mark(double size = 26)
    {
        Canvas canvas = new()
        {
            Width = 26,
            Height = 26
        };
        void Circle(double x, double y, double radius, string? fill, string? stroke)
        {
            Ellipse circle = new()
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = fill == null ? null : Theme.Brush(fill),
                Stroke = stroke == null ? null : Theme.Brush(stroke),
                StrokeThickness = 1.2
            };
            Canvas.SetLeft(circle, x - radius);
            Canvas.SetTop(circle, y - radius);
            canvas.Children.Add(circle);
        }
        Circle(13, 13, 11.5, null, "Text/25");
        Circle(13, 13, 7.5, null, "Text/45");
        Circle(13, 13, 3.5, "Accent", null);
        Circle(20.5, 8, 1.8, "Mint", null);
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }
    public static Grid Columns(Control left, Control right, double rightWidth = 340)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions($"*,20,{rightWidth}")
        };
        Grid.SetColumn(right, 2);
        left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
    public static Border Empty(string title, string detail, string icon = "devices", Control? action = null)
    {
        Control glyph = Tile(icon, "Primary", 44, "Accent");
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        StackPanel body = Stack(Space(glyph, 16), Space(Text(title, 16, bold: true), 6), Space(Text(detail, 13, "Muted"), action == null ? 0 : 18));
        body.MaxWidth = 640;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        if (action != null)
            body.Children.Add(action);
        return Card(body, 28);
    }
    /// <summary>Reassurance line: a mint icon, a short bold title and a muted explanation.</summary>
    public static DockPanel Point(string title, string detail, string icon = "shield")
    {
        Control glyph = Icon(icon, 18, "Mint");
        glyph.Margin = new(0, 1, 12, 0);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        DockPanel row = new();
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        row.Children.Add(Stack(Space(Text(title, 13, bold: true), 3), Text(detail, 12, "Muted")));
        return row;
    }
    /// <summary>A reassurance line on its own glass card.</summary>
    public static Border Note(string title, string detail, string icon = "shield") => Card(Point(title, detail, icon), new Thickness(20, 18));
    /// <summary>Round picture, or bold initials on warm amber when there is no picture or it cannot be decoded.</summary>
    public static Control Avatar(string name, byte[]? picture, double size = 36)
    {
        Border circle = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new(size / 2),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        Bitmap? image = Picture(picture, (int)(size * 2));
        if (image != null)
            circle.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else
        {
            circle.Background = new RadialGradientBrush { Center = new RelativePoint(0.36, 0.3, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.36, 0.3, RelativeUnit.Relative), RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative), GradientStops = [new(Theme.Color("#FFF0CF"), 0), new(Theme.Color("#F9C76A"), 0.4), new(Theme.Color("#EE9440"), 1)] };
            string initials = string.Concat(name.Split('(', '·')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
            TextBlock text = Text(initials.Length > 0 ? initials : "?", Math.Round(size * 0.34), "#2A1504", true);
            text.FontWeight = FontWeight.Bold;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            circle.Child = text;
        }
        Avalonia.Automation.AutomationProperties.SetName(circle, name + " picture");
        return circle;
    }
    public static Bitmap? Picture(byte[]? data, int pixels)
    {
        if (data == null)
            return null;
        try
        {
            using MemoryStream stream = new(data);
            return Bitmap.DecodeToWidth(stream, pixels, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or NotSupportedException) { return null; }
    }
    /// <summary>Avatar with a bold title and a muted detail line, used for PCs, browsers and senders.</summary>
    public static DockPanel Person(string name, byte[]? picture, string detail, double size = 40, string detailColor = "Muted")
    {
        Control avatar = Avatar(name, picture, size);
        avatar.Margin = new(0, 0, 14, 0);
        DockPanel row = new();
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        StackPanel text = Stack(Space(Text(name, 14, bold: true), 3), Text(detail, 12, detailColor));
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }
    public static Control Icon(string name, double size = 20, string color = "Primary")
    {
        string data = name switch
        {
            "home" => "M3,10 L12,3 L21,10 M5,9 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9",
            "send" => "M12,19 L12,5 M5,12 L12,5 L19,12",
            "transfer" => "M4,8 L18,8 L15,5 M20,16 L6,16 L9,19",
            "devices" => "M5,4 L19,4 A2,2 0 0 1 21,6 L21,14 A2,2 0 0 1 19,16 L5,16 A2,2 0 0 1 3,14 L3,6 A2,2 0 0 1 5,4 Z M9,20 L15,20 M12,16 L12,20",
            "folder" => "M3,7 A2,2 0 0 1 5,5 L9,5 L11,7 L19,7 A2,2 0 0 1 21,9 L21,17 A2,2 0 0 1 19,19 L5,19 A2,2 0 0 1 3,17 Z",
            "sync" => "M20,12 A8,8 0 0 1 6,17.3 M4,12 A8,8 0 0 1 18,6.7 M18,3 L18,7 L14,7 M6,21 L6,17 L10,17",
            "history" => "M12,4 A8,8 0 1 1 11.99,4 M12,8 L12,12 L15,14",
            "settings" => "M12,9 A3,3 0 1 1 11.99,9 M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9",
            "upload" => "M12,15 L12,4 M7,9 L12,4 L17,9 M4,15 L4,19 A1,1 0 0 0 5,20 L19,20 A1,1 0 0 0 20,19 L20,15",
            "download" => "M12,4 L12,15 M7,10 L12,15 L17,10 M4,15 L4,19 A1,1 0 0 0 5,20 L19,20 A1,1 0 0 0 20,19 L20,15",
            "pair" => "M9,8 L6,8 A4,4 0 0 0 6,16 L10,16 M15,8 L18,8 A4,4 0 0 1 18,16 L14,16 M8,12 L16,12",
            "lock" => "M7,11 L17,11 A2,2 0 0 1 19,13 L19,18 A2,2 0 0 1 17,20 L7,20 A2,2 0 0 1 5,18 L5,13 A2,2 0 0 1 7,11 Z M8,11 L8,8 A4,4 0 0 1 16,8 L16,11",
            "file" => "M7,3 L14,3 L19,8 L19,21 L7,21 Z M14,3 L14,8 L19,8",
            "phone" => "M9,3 L15,3 A2,2 0 0 1 17,5 L17,19 A2,2 0 0 1 15,21 L9,21 A2,2 0 0 1 7,19 L7,5 A2,2 0 0 1 9,3 Z M11,18 L13,18",
            "text" => "M5,6 L19,6 M5,11 L19,11 M5,16 L14,16",
            "menu" => "M4,7 L20,7 M4,12 L20,12 M4,17 L14,17",
            "close" => "M6,6 L18,18 M18,6 L6,18",
            "shield" => "M12,3 L20,6 L20,12 C20,17 16.5,20 12,21 C7.5,20 4,17 4,12 L4,6 Z",
            "check" => "M5,12 L10,17 L19,7",
            "plus" => "M12,5 L12,19 M5,12 L19,12",
            _ => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,13 M12,17 L12,18"
        };
        // The 24 px grid is kept (no stretch), so every glyph has the same optical weight as the design's icons.
        Avalonia.Controls.Shapes.Path path = new()
        {
            Data = Geometry.Parse(data),
            Stroke = Theme.Brush(color),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24
        };
        // The "icon" class lets a hovered button wiggle its glyph.
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = VerticalAlignment.Center, Classes = { "icon" } };
    }
}
