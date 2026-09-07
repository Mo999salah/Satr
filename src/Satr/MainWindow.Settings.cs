using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Satr;

public sealed partial class MainWindow
{
    private Window? _settingsWindow;
    private ListBox? _settingsNavigation;
    private void ShowSettings() => OpenSettings(0);

    private async void OpenSettings(int section)
    {
        if (_settingsWindow is not null)
        {
            _settingsNavigation!.SelectedIndex = section;
            _settingsWindow.Activate();
            return;
        }
        var dialog = new Window { Title = "Settings — Satr", Width = 760, Height = 580, MinWidth = 620, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Paint(dialog);
        _settingsWindow = dialog;
        var navigation = new ListBox { ItemsSource = new[] { "Terminal", "Arabic & text", "History", "Shortcuts", "About" }, Background = Brushes.Transparent };
        _settingsNavigation = navigation;
        var pages = new List<StackPanel>();
        StackPanel Page(string title, string description)
        {
            var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
            pages.Add(panel); return panel;
        }
        var terminal = Page("Terminal", "Font settings apply to all sessions.");
        var fonts = FontManager.Current.SystemFonts.Select(f => f.Name).Where(IsMonospaceFont).Append(_terminal.FontFamily.Name).Distinct().Order().ToArray();
        var family = new ComboBox { Name = "SettingsFont", ItemsSource = fonts, SelectedItem = _terminal.FontFamily.Name, HorizontalAlignment = HorizontalAlignment.Stretch };
        var size = new NumericUpDown { Name = "SettingsFontSize", Minimum = 10, Maximum = 32, Value = (decimal)_terminal.FontSize };
        terminal.Children.Add(new TextBlock { Text = "Monospace font" }); terminal.Children.Add(family);
        terminal.Children.Add(new TextBlock { Text = "Font size" }); terminal.Children.Add(size);
        var preview = new TextBlock { Text = "Satr > echo مرحبا بالعالم\nEnglish / مشروع / 123", TextWrapping = TextWrapping.Wrap, FontFamily = _terminal.FontFamily, FontSize = _terminal.FontSize };
        terminal.Children.Add(new Border { Child = preview, Background = Ui.Terminal, Padding = new Thickness(16), BorderBrush = Ui.Border, BorderThickness = new Thickness(1) });
        void Preview() { if (family.SelectedItem is string name) preview.FontFamily = new FontFamily(name); preview.FontSize = (double)(size.Value ?? 16); }
        family.SelectionChanged += (_, _) => Preview(); size.ValueChanged += (_, _) => Preview();
        var arabic = Page("Arabic & mixed text", "Control how Arabic and English share a terminal line. Input sent to the tool stays unchanged.");
        var rtl = new CheckBox { Name = "SettingsRtl", Content = "Smart RTL", IsChecked = _smartRtl };
        arabic.Children.Add(rtl);
        arabic.Children.Add(new TextBlock { Text = "Arrange mixed-script output for reading while keeping the terminal grid left-to-right.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted });
        var sample = new TerminalView { Height = 110, FontSize = 16, FontFamily = _terminal.FontFamily, Background = Ui.Terminal };
        var sampleBuffer = new TerminalBuffer(40, 3);
        var sampleSnapshot = sampleBuffer.Process("Satr | مرحبا بالعالم | English 123");
        // Use the actual terminal renderer so the preview reflects the setting.
        arabic.Children.Add(sample);
        void PreviewRtl() => sample.Present(sampleSnapshot, rtl.IsChecked == true, _cellWidth, _lineHeight, false);
        rtl.IsCheckedChanged += (_, _) => PreviewRtl();
        PreviewRtl();
        var historyPage = Page("History", "Limit the output kept in memory for each session.");
        var history = new ComboBox { Name = "SettingsHistory", ItemsSource = new[] { 2000, 5000, 10000 }, SelectedItem = _scrollbackRows, HorizontalAlignment = HorizontalAlignment.Stretch };
        historyPage.Children.Add(new TextBlock { Text = "Scrollback lines" }); historyPage.Children.Add(history);
        historyPage.Children.Add(new TextBlock { Text = "Reducing this limit discards older lines when you apply. Terminal output is not restored after closing Satr.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Warning });
        var shortcuts = Page("Keyboard shortcuts", "Search by action or key. Shortcuts are read-only.");
        var query = new TextBox { PlaceholderText = "Search shortcuts…" }; shortcuts.Children.Add(query);
        var results = new StackPanel { Spacing = 10 }; shortcuts.Children.Add(results);
        string[] entries = ["Copy selection — Ctrl+Shift+C", "Paste — Ctrl+V", "Select all — Ctrl+Shift+A", "Font size — Ctrl+plus / minus", "New shell — Ctrl+Shift+T", "Commands — Ctrl+Shift+P", "Next / previous session — Ctrl+Tab / Ctrl+Shift+Tab", "Jump to session — Ctrl+1…8", "Close session — Ctrl+Shift+W", "Start / reopen — Ctrl+Shift+R", "Move within project — Ctrl+Shift+PageUp / PageDown", "Search output — Ctrl+Shift+F", "Next / previous match — Enter / Shift+Enter", "Close search — Escape", "Previous / next prompt — Ctrl+Shift+Up / Down", "Select during mouse capture — Shift+drag", "Open link — Ctrl+Click"];
        void Filter()
        {
            results.Children.Clear();
            foreach (var entry in entries.Where(e => e.Contains(query.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)))
                results.Children.Add(new TextBlock { Text = entry, TextWrapping = TextWrapping.Wrap });
            if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "No matching shortcuts", Foreground = Ui.Muted });
        }
        query.TextChanged += (_, _) => Filter(); Filter();
        var about = Page("سطر / Satr", "A project terminal for Arabic, mixed text, and command-line tools.");
        about.Children.Add(new TextBlock { Text = $"Version {TerminalBuffer.ProductVersion}\nMohamad Salah\n\nWindows / Linux · Avalonia\nTerminal engine derived from RtlTerminal under MIT; see licenses.\n\nAI-tool compatibility varies by tool and version.", TextWrapping = TextWrapping.Wrap });
        var content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        navigation.SelectionChanged += (_, _) => content.Content = pages[Math.Clamp(navigation.SelectedIndex, 0, pages.Count - 1)];
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        buttons.Children.Add(Button("Cancel", () => dialog.Close()));
        buttons.Children.Add(Button("Apply", () =>
        {
            if (family.SelectedItem is string name) _terminal.FontFamily = new FontFamily(name);
            _terminal.FontSize = (double)(size.Value ?? 16); _smartRtl = rtl.IsChecked == true;
            _scrollbackRows = history.SelectedItem is int rows ? rows : 5000;
            foreach (var tab in _tabs) lock (tab) { tab.Snapshot = tab.Buffer.SetMaximumScrollbackRows(_scrollbackRows); tab.Dirty = true; }
            _terminal.ClearSelection(); MeasureFont(); Redraw(); RefreshChrome(); Persist(); dialog.Close();
        }));
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        navigation.Margin = new Thickness(10, 20); layout.Children.Add(navigation);
        Grid.SetColumn(content, 1); layout.Children.Add(content);
        Grid.SetRow(buttons, 1); Grid.SetColumnSpan(buttons, 2); layout.Children.Add(buttons);
        dialog.Content = layout; navigation.SelectedIndex = section;
        dialog.Closed += (_, _) => { _settingsWindow = null; _settingsNavigation = null; };
        await dialog.ShowDialog(this);
    }
}
