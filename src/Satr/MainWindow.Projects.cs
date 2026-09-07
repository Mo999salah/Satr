using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Satr;

public sealed partial class MainWindow
{
    private readonly List<SavedProject> _projects = [];
    private readonly Queue<string> _errors = new();
    private bool _saveTranscripts;

    private void RememberProject(string path)
    {
        if (_projects.Any(p => ProjectComparer.Equals(p.Path, path))) return;
        if (_projects.Count >= 200)
        {
            var old = _projects.FindLastIndex(p => !p.Pinned && !_tabs.Any(t => ProjectComparer.Equals(t.Project, p.Path)));
            if (old < 0) return;
            _projects.RemoveAt(old);
        }
        _projects.Add(new SavedProject(path));
    }

    private Control ProjectHeader(SavedProject project)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 2) };
        var title = Ui.Ghost((project.Collapsed ? "▸ " : "▾ ") + (project.Pinned ? "• " : "") + ProjectName(project.Path), () =>
        {
            var index = _projects.FindIndex(p => ProjectComparer.Equals(p.Path, project.Path));
            if (index < 0) return;
            _projects[index] = project with { Collapsed = !project.Collapsed };
            RefreshProjectGroups(); Persist();
        }, project.Path);
        title.Content = new TextBlock { Text = (project.Collapsed ? "▸ " : "▾ ") + (project.Pinned ? "• " : "") + ProjectName(project.Path), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        title.HorizontalAlignment = HorizontalAlignment.Stretch;
        title.HorizontalContentAlignment = HorizontalAlignment.Left;
        var menu = new ContextMenu();
        menu.Items.Add(Item(project.Pinned ? Ui.L("Unpin project") : Ui.L("Pin project"), () =>
        {
            var index = _projects.FindIndex(p => ProjectComparer.Equals(p.Path, project.Path));
            if (index < 0) return;
            _projects[index] = project with { Pinned = !project.Pinned }; RefreshProjectGroups(); Persist();
        }));
        menu.Items.Add(Item(Ui.L("Open folder"), () => OpenDirectory(project.Path)));
        menu.Items.Add(Item(Ui.L("Close project sessions"), () => CloseProject(project.Path)));
        menu.Items.Add(Item(Ui.L("Remove from recent projects"), () =>
        {
            if (_tabs.Any(t => ProjectComparer.Equals(t.Project, project.Path))) { Status("Close the project's sessions before removing it."); return; }
            _projects.RemoveAll(p => ProjectComparer.Equals(p.Path, project.Path)); RefreshProjectGroups(); RefreshChrome(); Persist();
        }));
        title.ContextMenu = menu; row.Children.Add(title);
        var add = Ui.Ghost("+", () => { }, "New session in " + project.Path);
        add.Click += (_, _) =>
        {
            _directory = project.Path;
            var choices = BuildNewSessionMenu(); choices.Open(add);
        };
        Avalonia.Automation.AutomationProperties.SetName(add, "New session in " + project.Path);
        Grid.SetColumn(add, 1); row.Children.Add(add);
        return row;
    }

    private async void CloseProject(string path)
    {
        var sessions = _tabs.Where(t => ProjectComparer.Equals(t.Project, path)).ToArray();
        if (sessions.Any(t => !t.Finished && t.Session is not null) && !await Confirm($"Close all {sessions.Length} sessions in {ProjectName(path)}? Running processes will stop.", "Close sessions")) return;
        foreach (var tab in sessions) await CloseTabAsync(tab, confirmed: true);
    }

    private void ActivateSession(Tab tab)
    {
        var index = _projects.FindIndex(p => ProjectComparer.Equals(p.Path, tab.Project));
        if (index >= 0 && _projects[index].Collapsed)
        {
            _projects[index] = _projects[index] with { Collapsed = false }; RefreshProjectGroups();
        }
        _tabStrip.SelectedItem = tab.Header; Select(tab); tab.Header.BringIntoView();
    }

    private void NavigateSession(int direction)
    {
        var ordered = _tabStrip.Items.OfType<ListBoxItem>().Where(i => i.Tag is Tab).Select(i => (Tab)i.Tag!).ToList();
        if (ordered.Count == 0) return;
        ActivateSession(ordered[(ordered.IndexOf(_active!) + direction + ordered.Count) % ordered.Count]);
    }

    private string SavedTranscript(Tab tab)
    {
        if (!_saveTranscripts) return "";
        if (tab.AwaitingLaunch) return tab.Transcript;
        string text;
        lock (tab) text = TerminalBuffer.LogicalText(tab.Buffer.CaptureSnapshot());
        // ponytail: bounded plain-text snapshot, not replayable terminal state; use a separate archive for longer history.
        var limit = Math.Min(262144, 2 * 1024 * 1024 / Math.Max(1, _tabs.Count));
        if (text.Length > limit)
        {
            var start = text.Length - limit;
            if (char.IsLowSurrogate(text[start])) start++;
            text = text[start..];
        }
        return text;
    }

    private async void BindConversation(Tab tab)
    {
        var id = new TextBox { Text = tab.ConversationId ?? "", PlaceholderText = "Conversation UUID; empty to use the tool picker" };
        var note = new TextBlock { Text = "Use an ID supplied by the tool. It takes effect on the next launch; Satr does not infer the current conversation.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted };
        var dialog = new Window { Title = Ui.L("Bind conversation"), Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Paint(dialog);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 }; panel.Children.Add(note); panel.Children.Add(id);
        panel.Children.Add(Ui.Ghost(Ui.L("Save"), () =>
        {
            var value = id.Text?.Trim();
            if (!string.IsNullOrEmpty(value) && !Guid.TryParse(value, out _)) { note.Text = "Enter a valid UUID."; return; }
            if (!tab.Closed) { tab.ConversationId = string.IsNullOrEmpty(value) ? null : value; Persist(); }
            dialog.Close();
        }));
        panel.Children.Add(Ui.Ghost(Ui.L("Cancel"), () => dialog.Close())); dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private void LogError(string text)
    {
        _errors.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {text}");
        while (_errors.Count > 100) _errors.Dequeue();
    }
}
