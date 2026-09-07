using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using Satr;

// Run on a desktop: dotnet run --project tests/Satr.UiChecks
// Uses an isolated workspace. Screenshots and results are left in the printed folder.
class UiChecks : Application
{
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "satr-ui-" + Guid.NewGuid().ToString("N"));
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Avalonia.Media.Color.Parse("#A3D9B1") };
        Styles.Add(theme);
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Satr/")) { Source = new Uri("avares://Satr/Theme.axaml") });
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
    private static void Call(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Shot(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window); using var stream = File.Create(Path.Combine(Output, name + ".png"));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow(); desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                await Task.Delay(1500);
                Call(window, "OpenSettings", 1);
                await Task.Delay(300);
                var settings = (Window)Field(window, "_settingsWindow");
                var rtlBox = settings.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "SettingsRtl");
                var originalRtl = (bool)Field(window, "_smartRtl");
                rtlBox.IsChecked = !originalRtl;
                Check((bool)Field(window, "_smartRtl") == originalRtl, "Settings edits must remain pending.");
                Shot(settings, "settings-arabic");
                var settingsNav = (ListBox)Field(window, "_settingsNavigation");
                foreach (var section in new[] { 0, 3, 5, 6, 7 })
                {
                    settingsNav.SelectedIndex = section;
                    await Task.Delay(100); Shot(settings, "settings-" + section);
                }
                settings.Close();
                Check((bool)Field(window, "_smartRtl") == originalRtl, "Cancel must preserve settings.");
                Call(window, "OpenSettings", 1);
                await Task.Delay(200);
                settings = (Window)Field(window, "_settingsWindow");
                settings.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "SettingsRtl").IsChecked = !originalRtl;
                settings.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Check((bool)Field(window, "_smartRtl") != originalRtl, "Apply must commit settings.");
                Call(window, "ToggleSmartRtl");
                var welcome = (Border)Field(window, "_welcome");
                Check(welcome.IsVisible, "Saved sessions must offer explicit launch.");
                Shot(window, "saved-session");
                var list = (ListBox)Field(window, "_tabStrip");
                Check(list.Items.Count == 3, "Restore sessions across both projects.");
                Check(!((Border)Field(((ListBoxItem)list.Items[1]!).Tag!, "ProjectHeading")).IsVisible, "Sessions in the same project must share one heading.");
                Check(((Border)Field(((ListBoxItem)list.Items[2]!).Tag!, "ProjectHeading")).IsVisible, "The second project needs its own heading.");
                list.SelectedIndex = 1;
                Call(window, "MoveTab", -1);
                Check(list.SelectedIndex == 0, "Moving a session must preserve its selection within the project.");
                Call(window, "MoveTab", 1);
                list.SelectedIndex = 2;
                Check(((TextBlock)Field(window, "_sessionSubtitle")).Text == Path.GetTempPath(), "Selection must update the project heading.");
                list.SelectedIndex = 0;
                var rtlBefore = (bool)Field(window, "_smartRtl");
                Call(window, "ToggleSmartRtl");
                Check((bool)Field(window, "_smartRtl") != rtlBefore, "Arabic layout control must change the real setting.");
                Call(window, "ToggleSmartRtl");
                Call(window, "RestartActive");
                await Task.Delay(1800);
                Check(!welcome.IsVisible, "Launching must reveal the terminal.");
                Call(window, "Send", "echo 'Satr UI check | مرحبا بالعالم | English 123'\r");
                await Task.Delay(1200);
                var buffer = (TerminalBuffer)Field(Field(window, "_active"), "Buffer");
                Check(TerminalBuffer.LogicalText(buffer.CaptureSnapshot()).Contains("Satr UI check"), "Shell output must reach the terminal.");
                typeof(MainWindow).GetField("_saveTranscripts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                Call(window, "Persist");
                Check(WorkspaceStore.Load(WorkspaceStore.StatePath).Tabs.Any(t => t.Transcript.Contains("Satr UI check")), "Opt-in transcript must persist output.");
                Shot(window, "running-wide");
                window.Width = 680; window.Height = 540;
                await Task.Delay(500);
                Check(((ListBoxItem)list.Items[0]!).Bounds.Height > 0, "Project sessions must remain laid out after resize.");
                Shot(window, "running-narrow");
                Call(window, "OpenSearch");
                await Task.Delay(300); Shot(window, "search-narrow");
                Call(window, "CloseSearch");
                Call(window, "ToggleSidebar");
                await Task.Delay(300);
                Check(!((Border)Field(window, "_sidebar")).IsVisible, "Sidebar must collapse.");
                Shot(window, "sidebar-collapsed");
                Call(window, "ToggleSidebar");
                Call(window, "Send", "exit\r");
                await Task.Delay(1000);
                var active = Field(window, "_active");
                await ((IAsyncDisposable)Field(active, "Session")).DisposeAsync();
                File.WriteAllText(Path.Combine(Output, "result.txt"), "PASS: restored projects, selection, explicit launch, terminal output, narrow layout, sidebar toggle, PTY cleanup.");
                var projectsBefore = WorkspaceStore.Load(WorkspaceStore.StatePath).Projects!.Length;
                Call(window, "CloseTab", active);
                await Task.Delay(300);
                Check(WorkspaceStore.Load(WorkspaceStore.StatePath).Projects!.Length == projectsBefore, "Closing a session must preserve its project.");
                Console.WriteLine("PASS " + Output);
                desktop.Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Output, "result.txt"), ex.ToString());
                Console.Error.WriteLine(ex); desktop.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        Environment.SetEnvironmentVariable("SATR_DATA_DIR", Output);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        File.WriteAllText(Path.Combine(Output, "workspace.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Tabs = new[] {
                new { Profile = "Shell", Directory = home, Project = home, Title = "Development", Draft = "", RightToLeft = true },
                new { Profile = "Shell", Directory = Path.GetTempPath(), Project = Path.GetTempPath(), Title = "تجربة عربية", Draft = "", RightToLeft = true },
                new { Profile = "Shell", Directory = home, Project = home, Title = "Build checks", Draft = "", RightToLeft = true }
            }
        }));
        AppBuilder.Configure<UiChecks>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
