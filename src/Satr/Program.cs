using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace Satr;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // Explicit opt-in keeps the established X11 path available on every desktop.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("SATR_BACKEND") == "wayland")
            builder = builder.UseWayland();
        builder.LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Ui.AccentColor;
        Resources["SystemAccentColorDark1"] = Color.Parse("#16A34A");
        Resources["SystemAccentColorLight1"] = Color.Parse("#4ADE80");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
