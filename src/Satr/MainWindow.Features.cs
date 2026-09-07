using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Satr;

public sealed partial class MainWindow
{
    private readonly TextBox _search = new() { PlaceholderText = Ui.L("Search session output"), MinWidth = 160, MaxLength = 512 };
    private readonly TextBlock _searchResult = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _matchCase = new() { Content = Ui.L("Match case"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _searchPanel = new()
    {
        IsVisible = false,
        Padding = new Thickness(10, 8),
        Margin = new Thickness(12, 10),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Background = Ui.Surface,
        BorderBrush = Ui.Border,
        ZIndex = 2
    };
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _directoryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _scrollbackRows = 5000;
    private double _normalWidth = 1220, _normalHeight = 820;
    private PixelPoint? _normalPosition;

    private Control BuildSearchBar()
    {
        AutomationProperties.SetName(_search, "Search terminal output");
        _searchResult.Foreground = Ui.Muted;
        _searchResult.FontSize = 12;
        var bar = new StackPanel { Spacing = 8 };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_searchResult);
        actions.Children.Add(_matchCase);
        actions.Children.Add(Button(Ui.L("Previous"), () => FindText(-1)));
        actions.Children.Add(Button(Ui.L("Next"), () => FindText(1)));
        actions.Children.Add(Button(Ui.L("Close"), CloseSearch));
        bar.Children.Add(_search); bar.Children.Add(actions);
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); _terminal.ResetSearch(); FindText(1); };
        _search.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _matchCase.IsCheckedChanged += (_, _) => { _terminal.ResetSearch(); FindText(1); };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindText(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; } };
        _searchPanel.Child = bar;
        return _searchPanel;
    }
    private void OpenSearch() { _searchPanel.IsVisible = true; _search.Focus(); _search.SelectAll(); }
    private void CloseSearch() { _searchPanel.IsVisible = false; _searchDebounce.Stop(); _terminal.Focus(); }
    private void JumpPrompt(int direction)
    {
        var rows = _active?.Snapshot?.PromptRows;
        if (rows is not { Count: > 0 })
        {
            Status("No command marks — shell did not emit OSC 133.");
            return;
        }
        var here = _active is { Follow: true } ? _active.Snapshot!.CursorRow : _terminal.FirstVisibleRow;
        int? target = null;
        if (direction < 0)
        {
            for (var i = rows.Count - 1; i >= 0; i--)
                if (rows[i] < here) { target = rows[i]; break; }
        }
        else
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i] > here) { target = rows[i]; break; }
        }
        if (target is null)
        {
            Status(direction < 0 ? "Already at first command." : "Already at last command.");
            return;
        }
        _terminal.ScrollToRow(target.Value);
    }
    private void FindText(int direction)
    {
        if (!_searchPanel.IsVisible) { OpenSearch(); return; }
        var (index, count) = _terminal.Find(_search.Text ?? "", direction, _matchCase.IsChecked == true);
        _searchResult.Text = count == 0 ? "No results" : $"{index} / {count}";
        if (_active is { } tab && count > 0) tab.Follow = false;
    }

    private void UpdateTab(Tab tab)
    {
        if (tab.Closed || _closed) return;
        var name = string.IsNullOrWhiteSpace(tab.CustomTitle) ? TabTitle(tab.Profile, tab.Project) : $"\u2068{tab.CustomTitle}\u2069";
        tab.Pip.Background = tab.Failed ? Ui.Danger : tab.Finished ? Ui.Muted : tab.Starting ? Ui.Warning : tab.AwaitingLaunch ? Ui.Muted : Ui.Accent;
        tab.Label.Text = string.IsNullOrWhiteSpace(tab.CustomTitle) ? ProfileCatalog.TabLabelOf(tab.Profile) : name;
        tab.Detail.Text = ProfileCatalog.TabLabelOf(tab.Profile) + " · " + (tab.Failed ? "Failed" : tab.Finished ? "Ended" : tab.Starting ? "Starting" : tab.AwaitingLaunch ? "Ready" : "Running");
        AutomationProperties.SetName(tab.Header, name + ", " + tab.State);
        ToolTip.SetTip(tab.Header, tab.Project + "\n" + tab.Directory + "\n" + tab.State + "\nRight-click: rename, folder, kill");
        if (ReferenceEquals(_active, tab)) Status(tab.State + " • " + tab.Profile);
        RefreshChrome();
    }

    private void RenameActive() { if (_active is { } tab) RenameTab(tab); }
    private async void RenameTab(Tab tab)
    {
        var name = new TextBox { Text = tab.CustomTitle ?? "", PlaceholderText = "Session name; leave empty for automatic", MaxLength = 100 };
        var dialog = new Window { Title = Ui.L("Rename session"), Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        Ui.Paint(dialog);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(name);
        panel.Children.Add(Button(Ui.L("Save"), () =>
        {
            var value = name.Text?.Trim();
            if (value?.Any(char.IsControl) == true) { StatusError("Name contains disallowed control characters."); return; }
            if (value?.Length > 100) { StatusError("Name exceeds 100 characters."); return; }
            if (!tab.Closed) { tab.CustomTitle = value; UpdateTab(tab); Persist(); }
            dialog.Close();
        }));
        dialog.Content = panel;
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        await dialog.ShowDialog(this);
    }
    private void MoveTab(int delta)
    {
        if (_active is not { } tab) return;
        var siblings = _tabs.Where(t => ProjectComparer.Equals(t.Project, tab.Project)).ToList();
        var position = siblings.IndexOf(tab) + delta;
        if (position < 0 || position >= siblings.Count) return;
        var oldIndex = _tabs.IndexOf(tab);
        var index = _tabs.IndexOf(siblings[position]);
        (_tabs[oldIndex], _tabs[index]) = (_tabs[index], _tabs[oldIndex]);
        RefreshProjectGroups();
        tab.Header.BringIntoView(); Persist();
    }
    private void RestartActive() { if (_active is { } tab) Restart(tab); }
    private async void Restart(Tab tab)
    {
        if (tab.Starting || tab.Closed) return;
        if (!ProfileCatalog.IsKnown(tab.Profile)) { StatusError("Unsupported profile: " + tab.Profile + ". Saved metadata is retained."); return; }
        if (!IsLaunchable(tab.Profile)) { ShowSetup(tab.Profile); return; }
        if (tab.Session is not null && !tab.Finished) { Status("Session still running. Force-kill it from the tab menu."); return; }
        if (tab.Buffer.CaptureSnapshot() is { Lines.Count: > 5 } && !await Confirm("Reopening clears the current screen history. Continue?", "Reopen")) return;
        tab.Starting = true;
        var previous = tab.Session; tab.Session = null;
        try
        {
            if (previous is not null) await previous.DisposeAsync();
            if (tab.Closed || _closing || _closed) return;
            lock (tab)
            {
                tab.Buffer = new TerminalBuffer(tab.Columns, tab.Rows, _scrollbackRows);
                tab.Snapshot = null; tab.Dirty = true; tab.Finished = false; tab.Failed = false; tab.SynchronizedSince = 0;
            }
            tab.Offset = 0; tab.Selection = (null, null); tab.Follow = true;
            if (_active == tab) { _terminal.Clear(); Render(); }
            tab.Starting = false; Start(tab);
        }
        catch (Exception ex) { tab.Starting = false; tab.Finished = true; tab.State = ex.Message; UpdateTab(tab); }
    }

    private void RememberWindow()
    {
        if (!_saveEnabled || WindowState != WindowState.Normal) return;
        _normalWidth = Bounds.Width; _normalHeight = Bounds.Height; _normalPosition = Position;
    }
    private void RestoreWindow(SavedWorkspace state)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var maxWidth = screen is null ? 3840 : screen.WorkingArea.Width / screen.Scaling;
        var maxHeight = screen is null ? 2160 : screen.WorkingArea.Height / screen.Scaling;
        Width = _normalWidth = Math.Clamp(double.IsFinite(state.WindowWidth) ? state.WindowWidth : 1220, MinWidth, Math.Max(MinWidth, maxWidth));
        Height = _normalHeight = Math.Clamp(double.IsFinite(state.WindowHeight) ? state.WindowHeight : 820, MinHeight, Math.Max(MinHeight, maxHeight));
        if (state.WindowX is int x && state.WindowY is int y && Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(x + 100, y + 40))))
            Position = new PixelPoint(x, y);
        _normalPosition = Position;
        if (state.Maximized) WindowState = WindowState.Maximized;
    }
    private void RefreshDirectory()
    {
        var changed = false;
        foreach (var tab in _tabs)
        {
            if (tab.Session is null || tab.Finished || tab.Closed) continue;
            var reported = tab.Snapshot?.WorkingDirectory;
            if (string.IsNullOrEmpty(reported) || !Path.IsPathFullyQualified(reported) || !System.IO.Directory.Exists(reported))
                continue;
            if (tab.Directory == reported) continue;
            tab.Directory = reported;
            changed = true;
            if (ReferenceEquals(_active, tab))
            {
                _path.Text = reported;
                ToolTip.SetTip(_path, "Project: " + tab.Project + "\nSession folder: " + reported + " — OSC 7 updates the session folder only");
            }
            UpdateTab(tab);
        }
        if (changed) Persist();
    }

    private async void PasteImage()
    {
        var tab = _active;
        if (tab is null || !IsAi(tab.Profile) || tab.Finished || tab.Session is null)
        { StatusError("Image pasting works only in AI sessions (Codex, Claude, Agy, Omp). Open one first."); return; }
        try
        {
            using var bitmap = Clipboard is { } clipboard ? await clipboard.TryGetBitmapAsync() : null;
            if (bitmap is null) { Status("Clipboard has neither image nor text."); return; }
            if (_active != tab || tab.Closed || tab.Finished) return;
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 32_000_000) { StatusError("Image too large: over 32 megapixels. Shrink it and retry."); return; }
            if (!await Confirm($"Paste a {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} image as a file path? The path is sent to the session.", "Paste image")) return;
            if (_active != tab || tab.Closed || tab.Finished) return;
            var directory = Path.Combine(WorkspaceStore.DataDirectory, "clipboard-images");
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
            var text = '"' + path.Replace("\\", "/") + '"';
            Send(ArabicInput.PreparePaste(text, tab.Snapshot?.Modes.BracketedPaste == true));
            Status("Image saved and its path pasted. Review before sending; image support depends on the CLI tool.");
        }
        catch (Exception ex) { StatusError(ex.Message); }
    }
}
