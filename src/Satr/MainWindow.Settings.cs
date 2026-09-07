using Avalonia;
using Avalonia.Input.Platform;
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
        var dialog = new Window { Title = Ui.L("Settings — Satr"), Width = 760, Height = 580, MinWidth = 620, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Paint(dialog);
        _settingsWindow = dialog;
        var navigation = new ListBox { ItemsSource = new[] { Ui.L("Terminal"), Ui.L("Arabic & text"), Ui.L("History"), Ui.L("Shortcuts"), Ui.L("About"), Ui.L("Tools"), Ui.L("Diagnostics"), Ui.L("Workspace") }, Background = Brushes.Transparent };
        _settingsNavigation = navigation;
        var pages = new List<StackPanel>();
        StackPanel Page(string title, string description)
        {
            var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
            pages.Add(panel); return panel;
        }
        var terminal = Page(Ui.L("Terminal"), Ui.L("Font settings apply to all sessions."));
        var fonts = FontManager.Current.SystemFonts.Select(f => f.Name).Where(IsMonospaceFont).Append(_terminal.FontFamily.Name).Append(_defaultTerminalFont).Distinct().Order().ToArray();
        var family = new ComboBox { Name = "SettingsFont", ItemsSource = fonts, SelectedItem = _terminal.FontFamily.Name, HorizontalAlignment = HorizontalAlignment.Stretch };
        var size = new NumericUpDown { Name = "SettingsFontSize", Minimum = 10, Maximum = 32, Value = (decimal)_terminal.FontSize };
        terminal.Children.Add(new TextBlock { Text = Ui.L("Monospace font") }); terminal.Children.Add(family);
        terminal.Children.Add(new TextBlock { Text = Ui.L("Font size") }); terminal.Children.Add(size);
        var preview = new TextBlock { Text = "Satr > echo مرحبا بالعالم\nEnglish / مشروع / 123", TextWrapping = TextWrapping.Wrap, FontFamily = _terminal.FontFamily, FontSize = _terminal.FontSize };
        terminal.Children.Add(new Border { Child = preview, Background = Ui.Terminal, Padding = new Thickness(16), BorderBrush = Ui.Border, BorderThickness = new Thickness(1) });
        void Preview() { if (family.SelectedItem is string name) preview.FontFamily = Ui.TerminalFont(name); preview.FontSize = (double)(size.Value ?? 16); }
        family.SelectionChanged += (_, _) => Preview(); size.ValueChanged += (_, _) => Preview();
        var arabic = Page(Ui.L("Arabic & mixed text"), Ui.L("Control how Arabic and English share a terminal line. Input sent to the tool stays unchanged."));
        var rtl = new CheckBox { Name = "SettingsRtl", Content = Ui.L("Smart RTL"), IsChecked = _smartRtl };
        arabic.Children.Add(rtl);
        arabic.Children.Add(new TextBlock { Text = Ui.L("Arrange mixed-script output for reading while keeping the terminal grid left-to-right."), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted });
        var sample = new TerminalView { Height = 110, FontSize = 16, FontFamily = _terminal.FontFamily, Background = Ui.Terminal };
        var sampleBuffer = new TerminalBuffer(40, 3);
        var sampleSnapshot = sampleBuffer.Process("Satr | مرحبا بالعالم | English 123");
        // Use the actual terminal renderer so the preview reflects the setting.
        arabic.Children.Add(sample);
        void PreviewRtl() => sample.Present(sampleSnapshot, rtl.IsChecked == true, _cellWidth, _lineHeight, false);
        rtl.IsCheckedChanged += (_, _) => PreviewRtl();
        PreviewRtl();
        var historyPage = Page(Ui.L("History"), Ui.L("Limit the output kept in memory for each session."));
        var saveOutput = new CheckBox { Content = Ui.L("Save a local text snapshot when saving the workspace"), IsChecked = _saveTranscripts };
        historyPage.Children.Add(saveOutput);
        var history = new ComboBox { Name = "SettingsHistory", ItemsSource = new[] { 2000, 5000, 10000 }, SelectedItem = _scrollbackRows, HorizontalAlignment = HorizontalAlignment.Stretch };
        historyPage.Children.Add(new TextBlock { Text = Ui.L("Scrollback lines") }); historyPage.Children.Add(history);
        historyPage.Children.Add(new TextBlock { Text = "Reducing this limit discards older lines when you apply. Saved snapshots may contain sensitive output. Up to 256K characters per session are retained for Readable transcript.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Warning });
        var shortcuts = Page(Ui.L("Keyboard shortcuts"), "Edit shortcuts using Ctrl or Alt. Ctrl+1…8 and search navigation remain reserved.");
        var query = new TextBox { PlaceholderText = Ui.L("Search shortcuts…") }; shortcuts.Children.Add(query);
        var results = new StackPanel { Spacing = 10 }; shortcuts.Children.Add(results);
        var shortcutEdits = _shortcuts.ToDictionary(p => p.Key, p => new TextBox { Text = p.Value, MinWidth = 150 });
        var shortcutRows = new Dictionary<string, Control>();
        foreach (var pair in shortcutEdits)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180") };
            row.Children.Add(new TextBlock { Text = Ui.L(pair.Key), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(pair.Value, 1); row.Children.Add(pair.Value);
            shortcutRows[pair.Key] = row; results.Children.Add(row);
        }
        query.TextChanged += (_, _) =>
        {
            foreach (var pair in shortcutRows) pair.Value.IsVisible = (Ui.L(pair.Key) + " " + shortcutEdits[pair.Key].Text).Contains(query.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
        };
        var about = Page("سطر / Satr", Ui.L("A project terminal for Arabic, mixed text, and command-line tools."));
        about.Children.Add(new TextBlock { Text = $"Version {TerminalBuffer.ProductVersion}\nMohamad Salah\n\nWindows / Linux · Avalonia\nTerminal engine derived from RtlTerminal under MIT; see licenses.\n\nAI-tool compatibility varies by tool and version.", TextWrapping = TextWrapping.Wrap });
        var tools = Page(Ui.L("Installed tools"), Ui.L("Check the tools available to this Satr process. Installation and authentication stay with each tool."));
        var toolList = new StackPanel { Spacing = 12 }; tools.Children.Add(toolList);
        void CheckTools()
        {
            toolList.Children.Clear();
            foreach (var tool in ProfileCatalog.All.Where(t => t.Kind == ProfileKind.Agent).DistinctBy(t => t.Executable))
            {
                var path = PtySession.FindExecutable(tool.Executable!);
                toolList.Children.Add(new TextBlock { Text = tool.Executable + " · " + (path is null ? "Not found" : "Installed"), FontWeight = FontWeight.SemiBold });
                toolList.Children.Add(new TextBlock { Text = path ?? tool.SetupHint, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted });
            }
        }
        tools.Children.Add(Button(Ui.L("Check again"), CheckTools)); CheckTools();
        tools.Children.Add(new TextBlock { Text = Ui.L("Restart Satr if an installer changed PATH."), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted });
        var diagnostics = Page(Ui.L("Diagnostics"), Ui.L("Recent errors in this window. Review before sharing: messages can contain local paths."));
        var errorText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, Text = string.Join("\n", _errors) };
        diagnostics.Children.Add(errorText);
        diagnostics.Children.Add(Button(Ui.L("Refresh"), () => errorText.Text = string.Join("\n", _errors)));
        diagnostics.Children.Add(Button(Ui.L("Copy log"), async () => { if (Clipboard is { } cb) await cb.SetTextAsync(errorText.Text ?? ""); }));
        var workspace = Page(Ui.L("Workspace"), Ui.L("Sidebar preferences are saved with your projects."));
        var language = new ComboBox { ItemsSource = new[] { "English", "العربية" }, SelectedIndex = Ui.Arabic ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        workspace.Children.Add(new TextBlock { Text = Ui.L("Interface language — reopen Satr to apply throughout") }); workspace.Children.Add(language);
        var sidebarSize = new NumericUpDown { Minimum = 200, Maximum = 400, Value = (decimal)_sidebarWidth };
        var hideSidebar = new CheckBox { Content = Ui.L("Hide sidebar"), IsChecked = _sidebarHidden };
        workspace.Children.Add(new TextBlock { Text = Ui.L("Sidebar width") }); workspace.Children.Add(sidebarSize); workspace.Children.Add(hideSidebar);
        var content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        navigation.SelectionChanged += (_, _) => content.Content = pages[Math.Clamp(navigation.SelectedIndex, 0, pages.Count - 1)];
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        var pending = new TextBlock { Foreground = Ui.Warning, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(pending);
        foreach (var edit in shortcutEdits.Values) edit.TextChanged += (_, _) => pending.Text = Ui.L("Unapplied changes");
        void Changed() => pending.Text = Ui.L("Unapplied changes");
        language.SelectionChanged += (_, _) => Changed();
        family.SelectionChanged += (_, _) => Changed(); size.ValueChanged += (_, _) => Changed();
        rtl.IsCheckedChanged += (_, _) => Changed(); history.SelectionChanged += (_, _) => Changed();
        saveOutput.IsCheckedChanged += (_, _) => Changed(); sidebarSize.ValueChanged += (_, _) => Changed(); hideSidebar.IsCheckedChanged += (_, _) => Changed();
        buttons.Children.Add(Button(Ui.L("Defaults"), () => { foreach (var pair in DefaultShortcuts) shortcutEdits[pair.Key].Text = pair.Value; family.SelectedItem = _defaultTerminalFont; size.Value = 16; rtl.IsChecked = true; history.SelectedItem = 5000; saveOutput.IsChecked = false; sidebarSize.Value = 256; hideSidebar.IsChecked = false; }));
        buttons.Children.Add(Button(Ui.L("Cancel"), () => dialog.Close()));
        buttons.Children.Add(Button(Ui.L("Apply"), () =>
        {
            try { _shortcuts = ValidateShortcuts(shortcutEdits.ToDictionary(p => p.Key, p => p.Value.Text ?? "")); }
            catch (Exception ex) { pending.Text = ex.Message; navigation.SelectedIndex = 3; return; }
            if (family.SelectedItem is string name) _terminal.FontFamily = Ui.TerminalFont(name);
            _terminal.FontSize = (double)(size.Value ?? 16); _smartRtl = rtl.IsChecked == true;
            Ui.Arabic = language.SelectedIndex == 1;
            _saveTranscripts = saveOutput.IsChecked == true;
            _sidebarWidth = (double)(sidebarSize.Value ?? 256); _sidebarHidden = hideSidebar.IsChecked == true; UpdateSidebarWidth();
            _scrollbackRows = history.SelectedItem is int rows ? rows : 5000;
            foreach (var tab in _tabs) lock (tab) { tab.Snapshot = tab.Buffer.SetMaximumScrollbackRows(_scrollbackRows); tab.Dirty = true; }
            _terminal.ClearSelection(); MeasureFont(); Redraw(); RefreshChrome();
            if (Persist()) { Status(Ui.L("Settings saved.")); dialog.Close(); }
            else pending.Text = Ui.L("Save failed — retry Apply");
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
