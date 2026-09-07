using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Satr;

/// <summary>Chrome tokens. Terminal cells stay on their own palette.</summary>
internal static class Ui
{
    public static readonly Color ChromeColor = Color.Parse("#0F172A");
    public static readonly Color TerminalColor = Color.Parse("#020617");
    public static readonly Color SurfaceColor = Color.Parse("#111827");
    public static readonly Color BorderColor = Color.Parse("#1E293B");
    public static readonly Color TextColor = Color.Parse("#F8FAFC");
    public static readonly Color MutedColor = Color.Parse("#94A3B8");
    public static readonly Color AccentColor = Color.Parse("#22C55E");
    public static readonly Color WarningColor = Color.Parse("#EAB308");
    public static readonly Color DangerColor = Color.Parse("#F87171");

    public static readonly IBrush Chrome = new SolidColorBrush(ChromeColor);
    public static readonly IBrush Terminal = new SolidColorBrush(TerminalColor);
    public static readonly IBrush Surface = new SolidColorBrush(SurfaceColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush Text = new SolidColorBrush(TextColor);
    public static readonly IBrush Muted = new SolidColorBrush(MutedColor);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush Warning = new SolidColorBrush(WarningColor);
    public static readonly IBrush Danger = new SolidColorBrush(DangerColor);

    public static readonly FontFamily Interface = new(
        OperatingSystem.IsWindows()
            ? "Segoe UI Variable, Segoe UI"
            : "IBM Plex Sans, Inter, Noto Sans, DejaVu Sans");

    public static Button Ghost(string text, Action action, string? tip = null)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text,
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        if (tip is not null) ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    public static Border Pip() => new()
    {
        Width = 8,
        Height = 8,
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
        Background = Accent
    };

    public static void Paint(Window window)
    {
        window.Background = Chrome;
        window.Foreground = Text;
        window.FontFamily = Interface;
        window.FlowDirection = FlowDirection.LeftToRight;
    }
}
