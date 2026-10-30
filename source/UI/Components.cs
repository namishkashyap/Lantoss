using System.Windows.Automation;
using System.Windows.Shapes;

namespace Lantoss.UI;

/// <summary>
/// Small WPF control factories. Use DynamicResource bindings for semantic colors so existing controls react immediately to appearance changes.
/// </summary>
internal static class C
{
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false)
    {
        TextBlock block = new()
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, color);
        return block;
    }
    /// <summary>Small uppercase group label (SHARE, MANAGE) from the hopo design.</summary>
    public static TextBlock Eyebrow(string text, Thickness margin)
    {
        TextBlock block = Text(text.ToUpperInvariant(), 11, "Muted");
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = margin;
        return block;
    }
    /// <summary>Page opening: eyebrow, 24 px title and one muted line, with an optional action on the right.</summary>
    public static DockPanel Hero(string eyebrow, string title, string subtitle, UIElement? action = null)
    {
        DockPanel panel = new() { Margin = new(0, 0, 0, 28) };
        if (action != null)
        {
            action.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            DockPanel.SetDock(action, Dock.Right);
            panel.Children.Add(action);
        }
        TextBlock heading = Text(title, 24);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new(0, 6, 0, 0);
        TextBlock detail = Text(subtitle, 13, "Muted");
        detail.MaxWidth = 640;
        detail.HorizontalAlignment = HorizontalAlignment.Left;
        detail.Margin = new(0, 8, 0, 0);
        panel.Children.Add(Stack(Eyebrow(eyebrow, new()), heading, detail));
        return panel;
    }
    public static StackPanel Stack(params UIElement[] elements)
    {
        StackPanel panel = new();
        foreach (UIElement item in elements)
            panel.Children.Add(item);
        return panel;
    }
    public static StackPanel Inline(params UIElement[] elements)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal
        };
        foreach (UIElement item in elements)
            panel.Children.Add(item);
        return panel;
    }
    public static T Space<T>(T element, double bottom = 16, double top = 0) where T : FrameworkElement
    {
        element.Margin = new(0, top, 0, bottom);
        return element;
    }
    public static Border Card(UIElement child, double padding = 22)
    {
        Border border = new()
        {
            Child = child,
            Padding = new(padding),
            Style = (Style)AppShell.Current.Window.FindResource("Card")
        };
        return border;
    }
    public static DockPanel Heading(string title, UIElement? right = null, double size = 16)
    {
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new(0, 0, 0, 16)
        };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            panel.Children.Add(right);
        }
        panel.Children.Add(Text(title, size, bold: true));
        return panel;
    }
    public static Button Button(string text, Action action, bool primary = false, string? icon = null)
    {
        Button button = new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (primary)
            button.Style = (Style)AppShell.Current.Window.FindResource("PrimaryButton");
        if (icon != null)
        {
            FrameworkElement image = Icon(icon, 15, primary ? "OnAccent" : "Primary");
            image.Margin = new(0, 0, 8, 0);
            TextBlock label = Text(text, 13, primary ? "OnAccent" : "Text");
            label.FontWeight = primary ? FontWeights.SemiBold : FontWeights.Medium;
            label.VerticalAlignment = VerticalAlignment.Center;
            button.Content = Inline(image, label);
        }
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => { try { action(); } catch (Exception e) { AppShell.Current.Error(e); } };
        return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false, string? icon = null)
    {
        Button button = Button(text, () => { }, primary, icon);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception e) { AppShell.Current.Error(e); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static Button Small(string text, Action action)
    {
        Button b = Button(text, action);
        b.Padding = new(12, 6, 12, 6);
        b.FontSize = 12;
        b.MinHeight = 34;
        return b;
    }
    public static Border Badge(string text, string color = "Primary", string background = "PrimarySoft")
    {
        Border border = new()
        {
            CornerRadius = new(999),
            Padding = new(10, 4, 10, 4),
            Child = Text(text, 11, color, true),
            VerticalAlignment = VerticalAlignment.Center
        };
        border.SetResourceReference(Border.BackgroundProperty, background);
        return border;
    }
    public static Grid Columns(UIElement left, UIElement right, double rightWidth = 340)
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new()
        {
            Width = new(20)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = new(rightWidth)
        });
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        grid.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 850;
            grid.ColumnDefinitions[1].Width = new(narrow ? 0 : 20);
            grid.ColumnDefinitions[2].Width = new(narrow ? 0 : rightWidth);
            Grid.SetColumn(right, narrow ? 0 : 2);
            Grid.SetRow(right, narrow ? 1 : 0);
            if (right is FrameworkElement element)
                element.Margin = new(0, narrow ? 20 : 0, 0, 0);
        };
        return grid;
    }
    public static Border Empty(string title, string detail, string icon = "devices", UIElement? action = null)
    {
        FrameworkElement image = Icon(icon, 35);
        image.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock heading = Text(title, 15, bold: true);
        heading.TextAlignment = TextAlignment.Center;
        heading.Margin = new(0, 14, 0, 8);
        TextBlock description = Text(detail, 12, "Muted");
        description.TextAlignment = TextAlignment.Center;
        description.MaxWidth = 470;
        StackPanel body = Stack(image, heading, description);
        body.Margin = new(20, 22, 20, 22);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        if (action != null)
        {
            action.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 18, 0, 0));
            action.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            body.Children.Add(action);
        }
        return Card(body);
    }
    public static Border Note(string title, string detail, string icon = "lock")
    {
        FrameworkElement graphic = Icon(icon, 19);
        graphic.Margin = new(0, 0, 12, 0);
        DockPanel panel = new();
        DockPanel.SetDock(graphic, Dock.Left);
        panel.Children.Add(graphic);
        panel.Children.Add(Stack(Text(title, 12, bold: true), Space(Text(detail, 11, "Muted"), 0, 5)));
        Border border = Card(panel, 18);
        border.CornerRadius = new(16);
        border.SetResourceReference(Border.BackgroundProperty, "PrimarySoft");
        return border;
    }
    /// <summary>Round picture, or initials when there is no picture or it cannot be decoded.</summary>
    public static FrameworkElement Avatar(string name, byte[]? picture, double size = 36)
    {
        Border circle = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new(size / 2)
        };
        ImageSource? image = Picture(picture, (int)(size * 2));
        if (image != null)
            circle.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else
        {
            circle.SetResourceReference(Border.BackgroundProperty, "Accent");
            string initials = string.Concat(name.Split('(', '·')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
            TextBlock text = Text(initials.Length > 0 ? initials : "?", Math.Round(size * 0.38), "OnAccent", true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            circle.Child = text;
        }
        AutomationProperties.SetName(circle, name + " picture");
        return circle;
    }
    public static ImageSource? Picture(byte[]? data, int pixels)
    {
        if (data == null)
            return null;
        try
        {
            System.Windows.Media.Imaging.BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = pixels;
            image.StreamSource = new MemoryStream(data);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is NotSupportedException or IOException or InvalidOperationException or ArgumentException) { return null; }
    }
    /// <summary>Avatar with a bold title and a muted detail line, used for PCs, browsers and senders.</summary>
    public static DockPanel Person(string name, byte[]? picture, string detail, double size = 40, string detailColor = "Muted")
    {
        FrameworkElement avatar = Avatar(name, picture, size);
        avatar.Margin = new(0, 0, 14, 0);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        DockPanel row = new();
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        StackPanel text = Stack(Space(Text(name, 15, bold: true), 4), Text(detail, 12, detailColor));
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }
    public static FrameworkElement Icon(string name, double size = 20, string color = "Primary")
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
            "wifi" => "M2,8 Q12,-1 22,8 M5,12 Q12,6 19,12 M8,16 Q12,12 16,16 M11,20 L13,20",
            "file" => "M5,2 L14,2 L20,8 L20,22 L5,22 Z M14,2 L14,8 L20,8 M9,13 L16,13 M9,17 L16,17",
            "check" => "M4,12 L9,17 L20,5",
            "plus" => "M12,3 L12,21 M3,12 L21,12",
            "close" => "M5,5 L19,19 M19,5 L5,19",
            "arrow" => "M3,12 L21,12 M15,6 L21,12 L15,18",
            "phone" => "M7,3 L17,3 L17,21 L7,21 Z M11,18 L13,18",
            "star" => "M12,2 L15,8 L22,9 L17,14 L18,22 L12,18 L6,22 L7,14 L2,9 L9,8 Z",
            _ => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,13 M12,17 L12,18"
        };
        System.Windows.Shapes.Path path = new()
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 24,
            Height = 24
        };
        if (color == "White")
            path.Stroke = Brushes.White;
        else
            path.SetResourceReference(Shape.StrokeProperty, color);
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = VerticalAlignment.Center };
    }
}
