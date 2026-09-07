using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Satr;

public sealed partial class MainWindow
{
    private readonly Grid _shell = new();
    private Border _sidebar = new();
    private bool _sidebarHidden;
    private static readonly StringComparer ProjectComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private Button _rtlIndicator = new();
    private readonly TextBlock _sessionTitle = new() { Text = "Workspace", FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sessionSubtitle = new() { Text = "Project terminal", FontSize = 12, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sessionCount = new() { Foreground = Ui.Muted, FontSize = 12 };
    private readonly Border _welcome = new() { Background = Ui.Terminal, IsVisible = false };
    private readonly TextBlock _welcomeTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _welcomeText = new() { Foreground = Ui.Muted, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
    private Button _launch = new();

    private Border BuildSidebar(Button fresh)
    {
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(14, 16, 14, 6) };
        var wordmark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 0, 0, 6) };
        var mark = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        mark.Children.Add(new Border { Width = 22, Height = 2, Background = Ui.Accent });
        mark.Children.Add(new Border { Width = 14, Height = 2, Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left });
        wordmark.Children.Add(mark);
        wordmark.Children.Add(new TextBlock { Text = "سطر", FontSize = 25, FontWeight = FontWeight.SemiBold });
        wordmark.Children.Add(new TextBlock { Text = "Satr", FontSize = 15, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(wordmark);
        fresh.Content = "+   New session";
        fresh.HorizontalAlignment = HorizontalAlignment.Stretch;
        fresh.HorizontalContentAlignment = HorizontalAlignment.Left;
        fresh.Background = Ui.Surface;
        top.Children.Add(fresh);
        var commands = Ui.Ghost("Commands", ShowPalette, "Ctrl+Shift+P");
        commands.HorizontalAlignment = HorizontalAlignment.Stretch;
        commands.HorizontalContentAlignment = HorizontalAlignment.Left;
        top.Children.Add(commands);
        var folder = Ui.Ghost("Open project…", async () => await ChooseDirectory());
        folder.HorizontalAlignment = HorizontalAlignment.Stretch;
        folder.HorizontalContentAlignment = HorizontalAlignment.Left;
        top.Children.Add(folder);
        _sessionCount.Margin = new Thickness(10, 14, 0, 0);
        top.Children.Add(_sessionCount);
        var bottom = new StackPanel { Spacing = 4, Margin = new Thickness(14, 12) };
        var settings = Ui.Ghost("Settings", ShowSettings);
        settings.HorizontalAlignment = HorizontalAlignment.Stretch;
        settings.HorizontalContentAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(settings);
        _rtlIndicator = Ui.Ghost("", () => OpenSettings(1), "Arabic and mixed-script settings");
        _rtlIndicator.FontSize = 12; _rtlIndicator.Foreground = Ui.Accent;
        bottom.Children.Add(_rtlIndicator);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(top);
        _tabStrip.Margin = new Thickness(10, 0);
        _tabStrip.Classes.Add("sessions");
        // At most 50 sessions: a regular panel keeps variable-height project headings stable on resize.
        _tabStrip.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel());
        Grid.SetRow(_tabStrip, 1); layout.Children.Add(_tabStrip);
        Grid.SetRow(bottom, 2); layout.Children.Add(bottom);
        return new Border { Background = new SolidColorBrush(Color.Parse("#141414")), BorderBrush = Ui.Border,
            BorderThickness = new Thickness(0, 0, 1, 0), Child = layout };
    }

    private Control BuildWelcome()
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 16, 24, 24), MaxWidth = 480 };
        panel.Children.Add(new Border { Width = 32, Height = 2, Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(_welcomeTitle);
        panel.Children.Add(_welcomeText);
        _launch = Ui.Ghost("Start session", () => { if (_active is not null) RestartActive(); else ShowPalette(); });
        _launch.Background = Ui.Surface; _launch.Foreground = Ui.Accent;
        _launch.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_launch);
        _welcome.Child = panel;
        return _welcome;
    }

    private void ToggleSidebar() { _sidebarHidden = !_sidebarHidden; UpdateSidebarWidth(); }
    private void UpdateSidebarWidth()
    {
        if (_shell.ColumnDefinitions.Count == 0) return;
        _sidebar.IsVisible = !_sidebarHidden;
        _shell.ColumnDefinitions[0].Width = new GridLength(_sidebarHidden ? 0 : Bounds.Width < 960 ? 220 : 256);
    }

    private static string ProjectName(string project)
    {
        var folder = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(project));
        return folder.Length == 0 ? project : folder;
    }

    private void ToggleSmartRtl() { _smartRtl = !_smartRtl; Redraw(); Persist(); RefreshChrome(); }

    private void RefreshProjectGroups()
    {
        var loading = _loading;
        var selected = _tabStrip.SelectedItem;
        _loading = true;
        try
        {
            _tabStrip.Items.Clear();
            foreach (var project in _tabs.GroupBy(t => t.Project, ProjectComparer))
            {
                var first = true;
                foreach (var tab in project)
                {
                    tab.ProjectHeading.IsVisible = first; first = false;
                    _tabStrip.Items.Add(tab.Header);
                }
            }
            if (selected is not null && _tabStrip.Items.Contains(selected)) _tabStrip.SelectedItem = selected;
        }
        finally { _loading = loading; }
    }

    private void RefreshChrome()
    {
        _sessionCount.Text = $"Projects  ·  {_tabs.Select(t => t.Project).Distinct(ProjectComparer).Count()}";
        _rtlIndicator.Content = _smartRtl ? "العربية · Smart RTL on" : "العربية · Smart RTL off";
        _sessionTitle.Text = _active?.CustomTitle ?? (_active is null ? "Workspace" : ProfileCatalog.TabLabelOf(_active.Profile));
        _sessionSubtitle.Text = _active?.Project ?? "Open a folder to work in";
        ToolTip.SetTip(_sessionSubtitle, _active?.Project);
        _welcome.IsVisible = _active is null || _active.AwaitingLaunch;
        var tool = _active is null ? "" : ProfileCatalog.TabLabelOf(_active.Profile);
        _welcomeTitle.Text = _active is null ? "No open sessions" : tool + " · Ready to start";
        _welcomeText.Text = _active is null ? "Open a project folder, or start a shell in your current folder." :
            "Launch " + tool + " in " + ProjectName(_active.Project) + ". Saved terminal output is not restored.";
        _launch.Content = _active is null ? "+   New session" : "Start " + tool;
        ToolTip.SetTip(_launch, "Start or reopen session — Ctrl+Shift+R");
    }
}
