using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lantoss.Desktop;

/// <summary>Small Avalonia control factories mirroring the Windows app's helpers, so both apps share one visual language.</summary>
internal static class C
{
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Foreground = Theme.Brush(color),
        TextWrapping = TextWrapping.Wrap
    };
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
    public static Border Card(Control child, double padding = 22) => new()
    {
        Child = child,
        Padding = new(padding),
        Background = Theme.Brush("Surface"),
        BorderBrush = Theme.Brush("Border"),
        BorderThickness = new(1),
        CornerRadius = new(16),
        BoxShadow = BoxShadows.Parse(Theme.Dark ? "0 6 22 0 #33000000" : "0 6 22 0 #0F0E211D")
    };
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
            Padding = new(16, 10),
            FontWeight = primary ? FontWeight.Bold : FontWeight.SemiBold,
            MinHeight = 40,
            CornerRadius = new(10),
            BorderThickness = new(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (primary)
            Theme.SetButton(button.Resources, "Accent", "OnAccent", "Accent", "Primary");
        button.Content = icon == null ? text : Inline(Icon(icon, 17, primary ? "OnAccent" : "Primary"), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
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
    public static Button Small(string text, Action action)
    {
        Button button = Button(text, action);
        button.Padding = new(11, 6);
        button.MinHeight = 32;
        button.FontSize = 12;
        return button;
    }
    /// <summary>Async lambdas bind here rather than to Action, so their exceptions are reported instead of crashing the app.</summary>
    public static Button Small(string text, Func<Task> action)
    {
        Button button = AsyncButton(text, action);
        button.Padding = new(11, 6);
        button.MinHeight = 32;
        button.FontSize = 12;
        return button;
    }
    public static Border Badge(string text, string color = "Primary", string background = "PrimarySoft") => new()
    {
        Child = Text(text, 11, color, true),
        Background = Theme.Brush(background),
        CornerRadius = new(7),
        Padding = new(9, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };
    public static Grid Columns(Control left, Control right, double rightWidth = 340)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions($"*,20,{rightWidth}")
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
    public static Border Empty(string title, string detail, string icon = "devices", Control? action = null)
    {
        Control glyph = Icon(icon, 34);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        StackPanel body = Stack(Space(glyph, 14), Space(Text(title, 17, bold: true), 8), Space(Text(detail, 13, "Muted"), action == null ? 0 : 18));
        if (action != null)
            body.Children.Add(action);
        return Card(body, 28);
    }
    public static Border Note(string title, string detail, string icon = "lock")
    {
        Control glyph = Icon(icon, 22);
        glyph.Margin = new(0, 2, 14, 0);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        DockPanel row = new();
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        row.Children.Add(Stack(Space(Text(title, 13, bold: true), 5), Text(detail, 12, "Muted")));
        return new Border
        {
            Child = row,
            Padding = new(18, 16),
            CornerRadius = new(14),
            Background = Theme.Brush("PrimarySoft")
        };
    }
    /// <summary>Round picture, or bold initials when there is no picture or it cannot be decoded.</summary>
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
            circle.Background = Theme.Brush("Accent");
            string initials = string.Concat(name.Split('(', '·')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
            TextBlock text = Text(initials.Length > 0 ? initials : "?", Math.Round(size * 0.38), "OnAccent", true);
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
        StackPanel text = Stack(Space(Text(name, 15, bold: true), 4), Text(detail, 12, detailColor));
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }
    public static Control Icon(string name, double size = 20, string color = "Primary")
    {
        string data = name switch
        {
            "home" => "M3,10 L12,3 L21,10 M5,9 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9",
            "send" => "M3,11 L21,3 L13,21 L10,14 Z M10,14 L21,3",
            "transfer" => "M3,7 L20,7 M16,3 L20,7 L16,11 M21,17 L4,17 M8,13 L4,17 L8,21",
            "devices" => "M3,3 L21,3 L21,16 L3,16 Z M12,16 L12,21 M8,21 L16,21",
            "folder" => "M3,6 L9,6 L11,9 L21,9 L21,21 L3,21 Z M3,10 L3,4 L9,4 L11,6 L20,6 L20,9",
            "sync" => "M20,8 A8,8 0 0 0 6,5 L3,8 M3,3 L3,8 L8,8 M4,16 A8,8 0 0 0 18,19 L21,16 M21,21 L21,16 L16,16",
            "history" => "M4,6 A9,9 0 1 1 3,14 M3,3 L3,8 L8,8 M12,7 L12,12 L16,14",
            "settings" => "M12,8 A4,4 0 1 1 11.99,8 M9,2 L15,2 L16,6 L20,7 L22,12 L19,15 L18,20 L13,22 L10,19 L5,18 L2,13 L5,10 L6,5 Z",
            "upload" => "M6,17 A5,5 0 0 1 5,7 A7,7 0 0 1 18,6 A5,5 0 0 1 20,16 M12,22 L12,10 M8,14 L12,10 L16,14",
            "download" => "M12,3 L12,16 M7,11 L12,16 L17,11 M4,18 L4,22 L20,22 L20,18",
            "pair" => "M9,8 L6,8 A4,4 0 0 0 6,16 L10,16 M15,8 L18,8 A4,4 0 0 1 18,16 L14,16 M8,12 L16,12",
            "lock" => "M5,10 L19,10 L19,22 L5,22 Z M8,10 L8,6 A4,4 0 0 1 16,6 L16,10 M12,15 L12,18",
            "file" => "M5,2 L14,2 L20,8 L20,22 L5,22 Z M14,2 L14,8 L20,8 M9,13 L16,13 M9,17 L16,17",
            "phone" => "M7,2 L17,2 L17,22 L7,22 Z M11,18 L13,18",
            "check" => "M4,12 L9,17 L20,5",
            "plus" => "M12,3 L12,21 M3,12 L21,12",
            _ => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,13 M12,17 L12,18"
        };
        Avalonia.Controls.Shapes.Path path = new()
        {
            Data = Geometry.Parse(data),
            Stroke = Theme.Brush(color),
            StrokeThickness = 1.65,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 24,
            Height = 24
        };
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = VerticalAlignment.Center };
    }
}
