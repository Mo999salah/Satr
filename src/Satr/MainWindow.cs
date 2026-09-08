using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Satr;

public sealed partial class MainWindow : Window
{
    private readonly TerminalView _terminal = new() { Background = Ui.Terminal, FontSize = 16,
        FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Cascadia Mono, JetBrains Mono, Consolas, Segoe UI" : "JetBrains Mono, DejaVu Sans Mono, Noto Sans Arabic") };
    private readonly string _defaultTerminalFont = OperatingSystem.IsWindows() ? "Cascadia Mono, JetBrains Mono, Consolas, Segoe UI" : "JetBrains Mono, DejaVu Sans Mono, Noto Sans Arabic";
    private readonly TextBlock _status = new() { Text = "Ready", TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _path = new() { TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
    private readonly ListBox _tabStrip = new() { Padding = new Thickness(0), Background = Brushes.Transparent };
    private readonly Grid _workspace = new();
    private readonly List<Tab> _tabs = [];
    private readonly DispatcherTimer _render = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string[]? _startupCommand;
    private FileStream? _instanceLock;
    private Tab? _active;
    private bool _loading, _saveEnabled, _closed, _closing, _smartRtl = true, _restoredFromBackup;
    private double _cellWidth = 9.6, _lineHeight = 24;
    private string _directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Retain legacy editor drafts in saved workspaces; removing the UI must not discard user data.
    private sealed class Tab(string profile, string directory, string draft, bool rtl)
    {
        public string Profile = profile, Directory = directory, Project = directory, Draft = draft;
        public string? CustomTitle;
        public string State = "Ready to start";
        public TextBlock Label = new();
        public TextBlock Detail = new();
        public Border ProjectHeading = new();
        public Border Pip = Ui.Pip();
        public string Transcript = "";
        public string? ConversationId;
        public LaunchPlan? Launch;
        public bool Transient;
        public bool Failed;
        public bool Rtl = rtl, Starting, Closed, Finished, Dirty = true, Follow = true, AwaitingLaunch;
        public int Columns = 80, Rows = 24;
        public TerminalBuffer Buffer = new(80, 24);
        public PtySession? Session;
        public TerminalSnapshot? Snapshot;
        public long SynchronizedSince;
        public double Offset;
        public ((int Row, int Offset)? Anchor, (int Row, int Offset)? End) Selection;
        public ListBoxItem Header = new();
    }

    public MainWindow(string[]? startupCommand = null)
    {
        _startupCommand = startupCommand;
        Title = "Satr"; Width = 1220; Height = 820; MinWidth = 680; MinHeight = 540;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Satr/Satr.ico")));
        Ui.Paint(this); FontSize = 13;
        var fresh = Ui.Ghost(Ui.L("New"), () => { }, "New session — Ctrl+Shift+T for shell, Ctrl+Shift+P for all types");
        fresh.ContextMenu = BuildNewSessionMenu();
        fresh.Click += (_, _) =>
        {
            if (fresh.ContextMenu is not ContextMenu menu) return;
            FillNewSessionMenu(menu);
            menu.Open(fresh);
        };
        var more = Ui.Ghost(Ui.L("More"), () => { }, "Copy, paste, settings, and the rest");
        var menu = new ContextMenu();
        menu.Items.Add(Item(Ui.L("Copy selection"), CopySelection));
        menu.Items.Add(Item(Ui.L("Paste into terminal"), Paste));
        menu.Items.Add(Item("Search — Ctrl+Shift+F", OpenSearch));
        menu.Items.Add(Item("Command palette — Ctrl+Shift+P", ShowPalette));
        menu.Items.Add(Item(Ui.L("Readable transcript"), ShowTranscript));
        menu.Items.Add(Item(Ui.L("Paste image as file path"), PasteImage));
        menu.Items.Add(Item(Ui.L("Copy folder path"), CopyPath));
        menu.Items.Add(Item(Ui.L("Open folder"), OpenPath));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Ui.L("Reopen finished session"), RestartActive));
        menu.Items.Add(Item(Ui.L("Rename session"), RenameActive));
        menu.Items.Add(Item(Ui.L("Move session up"), () => MoveTab(-1)));
        menu.Items.Add(Item(Ui.L("Move session down"), () => MoveTab(1)));
        menu.Items.Add(new Separator());
        more.ContextMenu = menu;
        more.Click += (_, _) => menu.Open(more);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Ui.Ghost(Ui.L("Search"), OpenSearch, "Search output — Ctrl+Shift+F"));
        actions.Children.Add(_restartButton = Ui.Ghost(Ui.L("Restart"), RestartActive, "Start or reopen this session"));
        actions.Children.Add(more);
        var heading = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(_sessionTitle);
        heading.Children.Add(_sessionSubtitle);
        var tabs = new DockPanel { Margin = new Thickness(20, 12), LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Right); tabs.Children.Add(actions);
        var toggle = Ui.Ghost("☰", ToggleSidebar, "Toggle sidebar");
        AutomationProperties.SetName(toggle, "Toggle sidebar");
        toggle.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(toggle, Dock.Left); tabs.Children.Add(toggle);
        tabs.Children.Add(heading);

        var surface = new Grid { RowDefinitions = new RowDefinitions("*") };
        _terminal.HorizontalAlignment = HorizontalAlignment.Stretch;
        _terminal.VerticalAlignment = VerticalAlignment.Stretch;
        surface.Children.Add(_terminal);
        surface.Children.Add(BuildSearchBar());
        surface.Children.Add(BuildWelcome());
        var host = new Border
        {
            Child = surface,
            Background = Ui.Terminal,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true,
            Padding = new Thickness(20, 12, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _workspace.RowDefinitions = new RowDefinitions("*");
        _workspace.Children.Add(host);

        var statusInner = new DockPanel { LastChildFill = true };
        _status.FontSize = 12;
        _path.FontSize = 12;
        DockPanel.SetDock(_status, Dock.Right);
        statusInner.Children.Add(_status);
        statusInner.Children.Add(_path);
        var statusBar = new Border
        {
            Child = statusInner,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 10),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(statusBar, "Click to jump to latest output");
        AutomationProperties.SetName(statusBar, "Session status. Click to jump to latest output");
        statusBar.PointerPressed += (_, _) =>
        {
            if (_active is { } tab) { tab.Follow = true; _terminal.ScrollToEnd(); Status(tab.State + " • " + tab.Profile + " • live"); }
        };

        var root = new Grid { Background = Ui.Chrome, RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(tabs);
        Grid.SetRow(_workspace, 1); root.Children.Add(_workspace);
        Grid.SetRow(statusBar, 2); root.Children.Add(statusBar);
        _shell.ColumnDefinitions = new ColumnDefinitions("256,*");
        _sidebar = BuildSidebar(fresh);
        _shell.Children.Add(_sidebar);
        Grid.SetColumn(root, 1); _shell.Children.Add(root);
        var resizeSidebar = new Avalonia.Controls.Primitives.Thumb { Width = 5, HorizontalAlignment = HorizontalAlignment.Right, Cursor = new Cursor(StandardCursorType.SizeWestEast) };
        resizeSidebar.DragDelta += (_, e) => { _sidebarWidth = Math.Clamp(_sidebarWidth + e.Vector.X, 200, 400); UpdateSidebarWidth(); };
        resizeSidebar.DragCompleted += (_, _) => Persist();
        ToolTip.SetTip(resizeSidebar, "Drag to resize sidebar"); _shell.Children.Add(resizeSidebar);
        Content = _shell;
        SizeChanged += (_, _) => UpdateSidebarWidth();
        _tabStrip.SelectionChanged += (_, _) => { if (!_loading && _tabStrip.SelectedItem is ListBoxItem { Tag: Tab tab }) Select(tab); };
        _render.Tick += (_, _) => Render();
        _errorClear.Tick += ClearError;
        _terminal.SizeChanged += (_, _) => ResizeTerminal();
        _terminal.ViewportChanged += ResizeTerminal;
        _terminal.FollowChanged += follow =>
        {
            if (_active is { } tab) tab.Follow = follow;
            if (!follow) Status("Review mode — click the status bar to jump back to live output.");
            else if (_active is { } live) Status(live.State + " • " + live.Profile);
        };
        _terminal.TextInput += (_, e) =>
        {
            // Linux IMEs may report Backspace/Delete as TextInput as well as KeyDown.
            // Control characters belong to the key path; sending both deletes twice.
            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl)) return;
            Send(e.Text); e.Handled = true;
        };
        _terminal.AddHandler(KeyDownEvent, TerminalKey, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, GlobalKey, RoutingStrategies.Tunnel);
        _terminal.GotFocus += (_, _) => { if (_active?.Snapshot?.Modes.FocusReporting == true) Send("\x1b[I"); };
        _terminal.LostFocus += (_, _) => { if (_active?.Snapshot?.Modes.FocusReporting == true) Send("\x1b[O"); };
        _terminal.AddHandler(PointerPressedEvent, MousePressed, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerReleasedEvent, MouseReleased, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerMovedEvent, MouseMoved, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerWheelChangedEvent, MouseWheel, RoutingStrategies.Tunnel);
        _terminal.LinkRequested += async uri => { try { await Launcher.LaunchUriAsync(uri); } catch (Exception ex) { StatusError(ex.Message); } };
        _directoryTimer.Tick += (_, _) => RefreshDirectory();
        PositionChanged += (_, _) => RememberWindow();
        SizeChanged += (_, _) => RememberWindow();
        Opened += (_, _) => { Restore(); OpenStartupCommand(); _directoryTimer.Start(); };
        Closing += OnClosing;
    }

    private static Button Button(string text, Action action, string? tip = null) => Ui.Ghost(text, action, tip);
    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item;
    }
    private ContextMenu BuildNewSessionMenu()
    {
        var menu = new ContextMenu();
        FillNewSessionMenu(menu);
        return menu;
    }
    private void FillNewSessionMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        foreach (var def in ProfileCatalog.All)
        {
            var presence = ProfileCatalog.Presence(def.Id, PtySession.FindExecutable);
            var item = Item(ProfileCatalog.MenuCaption(def, presence), () => OfferSession(def.Id));
            if (presence != ToolPresence.Installed)
                ToolTip.SetTip(item, def.Executable + " is not on PATH. Open setup to check again.");
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open in another folder…", async () => await ChooseDirectory()));
    }
    private void ShowPalette()
    {
        var dialog = new Window { Title = "Commands — Ctrl+Shift+P", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        Ui.Paint(dialog);
        var query = new TextBox { PlaceholderText = "Type a command… session, search, copy, settings", MaxLength = 100 };
        var list = new ListBox { MaxHeight = 320 };
        var commands = new List<(string Label, Action Run)>();
        foreach (var def in ProfileCatalog.All)
        {
            var presence = ProfileCatalog.Presence(def.Id, PtySession.FindExecutable);
            commands.Add((ProfileCatalog.PaletteCaption(def, presence), () => OfferSession(def.Id)));
        }
        commands.AddRange(
        [
            ("New project folder", async () => await ChooseDirectory()),
            ("Jump to previous command — Ctrl+Shift+Up", () => JumpPrompt(-1)),
            ("Jump to next command — Ctrl+Shift+Down", () => JumpPrompt(1)),
            ("Search terminal", OpenSearch),
            (Ui.L("Readable transcript"), ShowTranscript),
            (Ui.L("Copy selection"), CopySelection),
            (Ui.L("Paste into terminal"), Paste),
            ("Paste image as path", PasteImage),
            ("Copy session path", CopyPath),
            ("Open session folder", OpenPath),
            (Ui.L("Start or reopen session"), RestartActive),
            (Ui.L("Rename session"), RenameActive),
            ("Duplicate session", () => { if (_active is { } t) AddTab(t.Profile, t.Project, "", t.Rtl, project: t.Project); }),
            (Ui.L("Move session up"), () => MoveTab(-1)),
            (Ui.L("Move session down"), () => MoveTab(1)),
            ("Increase font", () => ChangeFont(1)),
            ("Decrease font", () => ChangeFont(-1)),
            (Ui.L("Settings"), ShowSettings),
            (Ui.L("Keyboard shortcuts"), ShowShortcuts),
        ]);
        void Refresh()
        {
            var q = query.Text?.Trim() ?? "";
            list.Items.Clear();
            foreach (var c in commands.Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase)))
                list.Items.Add(new ListBoxItem { Content = c.Label, Tag = c.Run });
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void RunSelected()
        {
            if (list.SelectedItem is ListBoxItem { Tag: Action run }) { dialog.Close(); run(); }
        }
        query.TextChanged += (_, _) => Refresh();
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { RunSelected(); e.Handled = true; }
            else if (e.Key == Key.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.Handled = true; }
            else if (e.Key == Key.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.Handled = true; }
        };
        list.DoubleTapped += (_, _) => RunSelected();
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        panel.Children.Add(query); panel.Children.Add(list);
        dialog.Content = panel;
        dialog.Opened += (_, _) => { Refresh(); query.Focus(); };
        dialog.Show(this);
    }
    private long _lastErrorTick;
    private readonly DispatcherTimer _errorClear = new() { Interval = TimeSpan.FromSeconds(8) };
    private void Status(string text)
    {
        // Keep a recent error visible; routine status must not erase it.
        if (Environment.TickCount64 - _lastErrorTick < 8000 && ReferenceEquals(_status.Foreground, Ui.Danger)) return;
        _errorClear.Stop();
        _status.Foreground = Ui.Muted; _status.Text = text;
    }
    private void StatusError(string text)
    {
        LogError(text);
        _lastErrorTick = Environment.TickCount64;
        _status.Foreground = Ui.Danger; _status.Text = text;
        _errorClear.Stop(); _errorClear.Start();
    }
    private void ClearError(object? sender, EventArgs e)
    {
        _errorClear.Stop();
        _lastErrorTick = 0;
        if (_active is { } tab) Status(tab.State + " • " + tab.Profile);
        else Status("Ready");
    }

    private static bool IsMonospaceFont(string family)
    {
        try
        {
            var typeface = new Typeface(new FontFamily(family));
            var widths = new[] { "M", "W", "i", "0" }.Select(text =>
                new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 16, Brushes.White).WidthIncludingTrailingWhitespace);
            var first = widths.First();
            var arabic = new FormattedText("مرحبا", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 16, Brushes.White).WidthIncludingTrailingWhitespace / 5;
            return widths.All(width => Math.Abs(width - first) < 0.25) && Math.Abs(arabic - first) < 0.25;
        }
        catch
        {
            return false;
        }
    }

    private void Restore()
    {
        try
        {
            WorkspaceStore.MigrateLegacyDirectory();
            System.IO.Directory.CreateDirectory(WorkspaceStore.DataDirectory);
            _instanceLock = new FileStream(Path.Combine(WorkspaceStore.DataDirectory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) { StatusError("Could not open the workspace; it may be open in another window. " + ex.Message); _workspace.IsEnabled = false; return; }
        _loading = true;
        try
        {
            var read = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
            var state = read.Workspace;
            _terminal.FontSize = double.IsFinite(state.FontSize) ? Math.Clamp(state.FontSize, 10, 32) : 16;
            _smartRtl = state.SmartRtl;
            if (!string.IsNullOrWhiteSpace(state.FontFamily) && state.FontFamily.Length <= 256 && IsMonospaceFont(state.FontFamily))
                _terminal.FontFamily = Ui.TerminalFont(state.FontFamily);
            _scrollbackRows = state.ScrollbackRows is 2000 or 5000 or 10000 ? state.ScrollbackRows : 5000;
            _projects.AddRange(state.Projects ?? []);
            _sidebarHidden = state.SidebarHidden;
            _sidebarWidth = double.IsFinite(state.SidebarWidth) ? Math.Clamp(state.SidebarWidth, 200, 400) : 256;
            _saveTranscripts = state.SaveTranscripts;
            _shortcuts = ValidateShortcuts(state.Shortcuts);
            RestoreWindow(state); UpdateSidebarWidth();
            MeasureFont();
            foreach (var tab in state.Tabs)
                AddTab(ResumeOf(tab.Profile), tab.Directory, tab.Draft, tab.RightToLeft, tab.Title, tab.Project, tab.Transcript, tab.ConversationId);
            RefreshProjectGroups();
            if (_tabs.Count > 0) _tabStrip.SelectedItem = _tabs[Math.Clamp(state.SelectedTab, 0, _tabs.Count - 1)].Header;
            _saveEnabled = true;
            _restoredFromBackup = read.FromBackup;
        }
        catch (Exception ex) { StatusError("Restore failed. Auto-save is off to protect the original file. " + ex.Message); }
        finally { _loading = false; }
        if (_tabs.Count == 0 && _projects.Count == 0) AddTab("Shell");
        else if (_tabStrip.SelectedItem is ListBoxItem { Tag: Tab tab }) Select(tab);
        if (_restoredFromBackup)
            Status("Workspace recovered from a backup copy. Ctrl+Shift+R launches a tab.");
        RefreshChrome(); MeasureFont(); _render.Start();
    }

    private void OpenStartupCommand()
    {
        if (_startupCommand is not { Length: > 0 }) return;
        try
        {
            var plan = PtySession.ResolveExternalCommand(_startupCommand, PtySession.FindExecutable);
            AddTab("Shell", title: Path.GetFileName(plan.App), launch: plan, transient: true);
        }
        catch (Exception ex) { StatusError("Command was not started: " + ex.Message); }
    }

    private void AddTab(string profile, string? directory = null, string draft = "", bool rtl = true, string? title = null, string? project = null, string transcript = "", string? conversationId = null, LaunchPlan? launch = null, bool transient = false)
    {
        if (_instanceLock is null) return;
        if (_tabs.Count >= 50) { StatusError("Maximum 50 tabs."); return; }
        profile = ResolveProfile(profile, _loading);
        var available = IsLaunchable(profile);
        if (!ShouldCreateTab(profile, _loading, available))
        {
            StatusError($"{ToolFor(profile)} is not found in PATH. Install it first, or open a plain shell.");
            if (!_loading && _tabs.Count == 0) AddTab("Shell", directory, draft, rtl, title, project);
            return;
        }
        var cwd = directory ?? _directory;
        var tab = new Tab(profile, cwd, draft, rtl)
        {
            CustomTitle = title, Transcript = transcript, ConversationId = conversationId,
            Project = string.IsNullOrWhiteSpace(project) ? cwd : project, Launch = launch, Transient = transient,
            AwaitingLaunch = _loading
        };
        if (tab.AwaitingLaunch)
            tab.State = "Ready to start — Ctrl+Shift+R to launch.";
        tab.Buffer.SetMaximumScrollbackRows(_scrollbackRows);
        var label = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), HorizontalAlignment = HorizontalAlignment.Stretch };
        tab.Label = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
            FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Text };
        label.Children.Add(tab.Pip);
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(tab.Label);
        tab.Detail = new TextBlock { FontSize = 11, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(tab.Detail);
        Grid.SetColumn(text, 1); label.Children.Add(text);
        tab.Pip.Margin = new Thickness(0, 0, 10, 0);
        var close = Button("×", () => CloseTab(tab), "Close tab — Ctrl+Shift+W");
        close.Padding = new Thickness(8, 2);
        close.Foreground = Ui.Muted;
        close.FontSize = 14;
        AutomationProperties.SetName(close, "Close " + TabTitle(profile, tab.Project));
        Grid.SetColumn(close, 2); label.Children.Add(close);
        var row = new Border { Child = label, Padding = new Thickness(10, 7), CornerRadius = new CornerRadius(4) };
        row.Classes.Add("session-row");
        var projectLabel = new TextBlock { Text = ProjectName(tab.Project), FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        tab.ProjectHeading = new Border { Child = projectLabel, Padding = new Thickness(10, 18, 10, 7) };
        ToolTip.SetTip(tab.ProjectHeading, tab.Project);
        var entry = new StackPanel(); entry.Children.Add(tab.ProjectHeading); entry.Children.Add(row);
        tab.Header = new ListBoxItem { Content = entry, Tag = tab };
        var tabMenu = new ContextMenu();
        tabMenu.Items.Add(Item(Ui.L("Rename session"), () => RenameTab(tab)));
        tabMenu.Items.Add(Item(Ui.L("Duplicate session in same folder"), () => AddTab(tab.Profile, tab.Project, "", tab.Rtl, project: tab.Project)));
        tabMenu.Items.Add(Item(Ui.L("Copy folder path"), () => CopyDirectory(tab.Directory)));
        tabMenu.Items.Add(Item(Ui.L("Open folder"), () => OpenDirectory(tab.Directory)));
        tabMenu.Items.Add(Item(Ui.L("Start or reopen session"), () => Restart(tab)));
        if (ToolFor(profile) is "codex" or "omp") tabMenu.Items.Add(Item(Ui.L("Bind conversation ID…"), () => BindConversation(tab)));
        tabMenu.Items.Add(Item(Ui.L("Force-kill session"), () => ForceKill(tab)));
        tab.Header.ContextMenu = tabMenu;
        UpdateTab(tab);
        if (!tab.Transient) RememberProject(tab.Project);
        _tabs.Add(tab);
        RefreshProjectGroups();
        RefreshChrome();
        if (!_loading) { _tabStrip.SelectedItem = tab.Header; Select(tab); Persist(); }
    }

    internal static bool IsAi(string profile) => ProfileCatalog.IsAgent(profile);
    internal static bool IsFreshAi(string profile) => ProfileCatalog.IsFreshAgent(profile);
    internal static string ToolFor(string profile) => ProfileCatalog.ToolName(profile);
    internal static string ResumeOf(string profile) => ProfileCatalog.ResumeOf(profile);
    internal static string? FreshOf(string profile) => ProfileCatalog.FreshOf(profile);
    internal static string ResolveProfile(string profile, bool restoring) =>
        restoring || ProfileCatalog.IsKnown(profile) ? profile : "Shell";
    internal static bool IsLaunchable(string profile) =>
        ProfileCatalog.IsLaunchable(profile, PtySession.FindExecutable);
    internal static bool ShouldCreateTab(string profile, bool restoring, bool available) =>
        restoring || available;
    internal static bool ShouldAutoStart(string profile, bool available, bool restored = false) =>
        !restored && available && (profile == "Shell" || IsAi(profile));
    internal static string LaunchDirectory(string profile, string project, string directory) =>
        IsAi(profile)
            ? (System.IO.Directory.Exists(project) ? project : directory)
            : (System.IO.Directory.Exists(directory) ? directory : project);

    internal static string TabTitle(string profile, string directory)
    {
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        var name = ProfileCatalog.TabLabelOf(profile);
        return $"\u2068{(folder.Length == 0 ? directory : folder)}\u2069 · \u2068{name}\u2069";
    }

    private void SaveActive()
    {
        if (_active is not { } tab) return;
        tab.Offset = _terminal.VerticalOffset; tab.Selection = _terminal.SelectionState;
    }

    private void Select(Tab tab)
    {
        if (ReferenceEquals(_active, tab)) return;
        SaveActive(); _active = tab; _directory = tab.Project;
        _path.Text = tab.Directory;
        ToolTip.SetTip(_path, "Project: " + tab.Project + "\nSession folder: " + tab.Directory + " — OSC 7 updates the session folder only");
        _terminal.Clear(); tab.Dirty = true; Render();
        _terminal.SelectionState = tab.Selection;
        if (!tab.Follow) { _terminal.ScrollToVerticalOffset(tab.Offset); Status("Review mode — scroll/drag down or press Enter to return to live output."); }
        ResizeTerminal();
        if (!_searchPanel.IsVisible) _terminal.Focus();
        if (tab.Session is null && !tab.Starting && !tab.Finished &&
            ShouldAutoStart(tab.Profile, IsLaunchable(tab.Profile), tab.AwaitingLaunch))
            Start(tab);
        UpdateTab(tab);
        Persist();
    }

    private async void Start(Tab tab)
    {
        tab.AwaitingLaunch = false;
        tab.Starting = true; tab.State = "Starting…"; UpdateTab(tab);
        if (ReferenceEquals(_active, tab)) Status("Starting session…");
        try
        {
            var directory = LaunchDirectory(tab.Profile, tab.Project, tab.Directory);
            var session = tab.Launch is { } launch
                ? await PtySession.Start(launch, directory, tab.Columns, tab.Rows, _shutdown.Token)
                : await PtySession.Start(tab.Profile, directory, tab.Columns, tab.Rows, _shutdown.Token, tab.ConversationId);
            if (_closed || _closing || tab.Closed) { await session.DisposeAsync(); return; }
            tab.Session = session;
            session.Output += async text =>
            {
                TerminalSnapshot snapshot;
                lock (tab) { snapshot = tab.Buffer.Process(text); tab.Snapshot = snapshot; tab.Dirty = true; }
                if (snapshot.Responses.Count > 0) await session.WriteResponsesAsync(snapshot.Responses);
            };
            session.Ended += message => Dispatcher.UIThread.Post(() =>
            {
                if (tab.Session != session || tab.Closed || _closed) return;
                tab.Finished = true; tab.Failed = !message.StartsWith("Session ended", StringComparison.Ordinal); tab.State = message; UpdateTab(tab);
                if (ReferenceEquals(_active, tab)) { if (tab.Failed) StatusError(message); else Status(message); }
            });
            session.ReadOutput();
            session.Resize(tab.Columns, tab.Rows);
            tab.State = "Running"; UpdateTab(tab);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { tab.Finished = true; tab.Failed = true; tab.State = "Failed to start: " + ex.Message; UpdateTab(tab); if (ReferenceEquals(_active, tab)) StatusError(tab.State); }
        finally { tab.Starting = false; UpdateTab(tab); }
    }

    private bool Persist()
    {
        if (_loading) return true;
        SaveActive();
        if (!_saveEnabled) return false;
        try
        {
            var savedTabs = _tabs.Where(tab => !tab.Transient).ToArray();
            var selected = Array.IndexOf(savedTabs, _active);
            WorkspaceStore.Save(WorkspaceStore.StatePath, new SavedWorkspace(savedTabs.Select(tab =>
                new SavedTab(tab.Profile, tab.Directory, tab.Draft, tab.Rtl, tab.CustomTitle, tab.Project, SavedTranscript(tab), tab.ConversationId)).ToArray(), Math.Max(0, selected), _terminal.FontSize, _smartRtl,
                _terminal.FontFamily.Name, _normalWidth, _normalHeight, _normalPosition?.X, _normalPosition?.Y, WindowState == WindowState.Maximized, _scrollbackRows, WorkspaceStore.CurrentSchema, _projects.ToArray(), _sidebarHidden, _sidebarWidth, _saveTranscripts, Ui.Arabic, _shortcuts));
            return true;
        }
        catch (Exception ex) { StatusError("Could not save sessions; check disk space and data-folder permissions. " + ex.Message); return false; }
    }

    private async void CloseTab(Tab tab) => await CloseTabAsync(tab);

    private async Task CloseTabAsync(Tab tab, bool confirmed = false)
    {
        if (!confirmed && !tab.Finished && tab.Session is not null)
        {
            if (!await Confirm($"Close a running tab?\n{tab.Directory}\nThe session stops immediately.", Ui.L("Close"))) return;
        }
        SaveActive();
        try
        {
            if (tab.Draft.Length > 0)
            {
                var archive = Path.Combine(WorkspaceStore.DataDirectory, "drafts"); System.IO.Directory.CreateDirectory(archive);
                await File.WriteAllTextAsync(Path.Combine(archive, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".txt"), tab.Draft, Encoding.UTF8);
            }
        }
        catch (Exception ex) { StatusError("Tab not closed: draft archiving failed: " + ex.Message); return; }
        tab.Closed = true;
        _tabs.Remove(tab); RefreshProjectGroups();
        if (ReferenceEquals(_active, tab))
        {
            _active = null;
            if (_tabs.Count > 0) { _tabStrip.SelectedItem = _tabs[0].Header; Select(_tabs[0]); }
            else { _terminal.Clear(); _path.Text = ""; Status("Open a new shell to start."); }
        }
        RefreshChrome();
        Persist();
        if (tab.Session is { } session) { try { await session.DisposeAsync(); } catch (Exception ex) { StatusError(ex.Message); } }
    }

    private async void ForceKill(Tab tab)
    {
        if (tab.Session is null || tab.Finished) { Restart(tab); return; }
        if (!await Confirm("Force-kill the session? Unsaved screen output is lost.", "Kill")) return;
        var s = tab.Session; tab.Session = null;
        try { await s.DisposeAsync(); }
        catch (Exception ex)
        {
            tab.Session = s; tab.Finished = true; tab.State = "Close failed: " + ex.Message;
            UpdateTab(tab); StatusError(tab.State); return;
        }
        tab.Finished = true; tab.State = "Killed — Ctrl+Shift+R to reopen."; UpdateTab(tab);
        if (ReferenceEquals(_active, tab)) StatusError(tab.State);
    }

    private static async Task<bool> Confirm(string text, string ok)
    {
        var dialog = new Window { Title = Ui.L("Confirm"), Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        Ui.Paint(dialog);
        var done = false;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(Button(Ui.L("Cancel"), () => dialog.Close()));
        row.Children.Add(Button(ok, () => { done = true; dialog.Close(); }));
        panel.Children.Add(row); dialog.Content = panel;
        var owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
        return done;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true; if (_closing) return;
        RefreshDirectory();
        if (!Persist())
        {
            if (!await Confirm("Could not save sessions. Close without saving? Recent tab order is lost.", "Close without saving")) return;
        }
        if (_tabs.Any(t => !t.Finished && t.Session is not null))
        {
            if (!await Confirm($"Close {_tabs.Count(t => !t.Finished && t.Session is not null)} running session(s)?", "Close all")) return;
        }
        _closing = true; _shutdown.Cancel(); _render.Stop(); _directoryTimer.Stop();
        try { await Task.WhenAll(_tabs.Where(tab => tab.Session is not null).Select(tab => tab.Session!.DisposeAsync().AsTask())); }
        catch (Exception ex)
        {
            StatusError("Session cleanup failed: " + ex.Message);
            if (!await Confirm("A session did not finish closing. Close the window anyway? Its process may still be running.", "Close window"))
            {
                _closing = false; _render.Start(); return;
            }
        }
        _instanceLock?.Dispose(); _closed = true; Close();
    }

    private void Render()
    {
        if (_active is not { } tab) return;
        TerminalSnapshot snapshot;
        lock (tab)
        {
            if (!tab.Dirty) return;
            snapshot = tab.Snapshot ?? tab.Buffer.CaptureSnapshot();
            if (snapshot.Modes.SynchronizedOutput)
            {
                if (tab.SynchronizedSince == 0) tab.SynchronizedSince = Environment.TickCount64;
                if (Environment.TickCount64 - tab.SynchronizedSince < 150) return;
            }
            tab.SynchronizedSince = 0; tab.Dirty = false;
        }
        _terminal.Present(snapshot, _smartRtl, _cellWidth, _lineHeight, tab.Follow || snapshot.Modes.AlternateScreen);
    }

    private void Redraw() { if (_active is { } tab) { lock (tab) tab.Dirty = true; Render(); } }
    private void MeasureFont()
    {
        var sample = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_terminal.FontFamily), _terminal.FontSize, Brushes.White);
        _cellWidth = Math.Max(4, sample.WidthIncludingTrailingWhitespace); _lineHeight = Math.Ceiling(Math.Max(sample.Height, _terminal.FontSize * 1.5));
        ResizeTerminal(); Redraw();
    }
    private void ChangeFont(int delta) { _terminal.FontSize = Math.Clamp(_terminal.FontSize + delta, 10, 32); MeasureFont(); Persist(); }
    private void ResizeTerminal()
    {
        if (_active is not { } tab || _terminal.ViewportWidth <= 0 || _terminal.ViewportHeight <= 0) return;
        var columns = Math.Max(10, (int)(_terminal.ViewportWidth / _cellWidth));
        var rows = Math.Max(5, (int)(_terminal.ViewportHeight / _lineHeight));
        tab.Buffer.SetCellMetrics(_cellWidth, _lineHeight);
        if (tab.Columns == columns && tab.Rows == rows) return;
        tab.Columns = columns; tab.Rows = rows;
        var before = tab.Snapshot ?? tab.Buffer.CaptureSnapshot();
        var selection = _terminal.SelectionState;
        lock (tab) { tab.Snapshot = tab.Buffer.Resize(columns, rows); tab.Dirty = true; }
        tab.Selection = TerminalBuffer.RemapSelection(before, selection.Anchor, selection.End, tab.Snapshot);
        _terminal.SelectionState = tab.Selection;
        try { tab.Session?.Resize(columns, rows); } catch (Exception ex) { StatusError(ex.Message); }
    }
    private async Task ChooseDirectory()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Ui.L("Project folder"), AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } directory) return;
            _directory = directory; RememberProject(directory); RefreshProjectGroups(); RefreshChrome(); Persist();
            if (await ChooseProfile() is { } chosen) OfferSession(chosen);
        }
        catch (Exception ex) { StatusError(ex.Message); }
    }
    private void OfferSession(string profile)
    {
        if (IsLaunchable(profile) || ProfileCatalog.Find(profile) is null)
        {
            AddTab(profile);
            return;
        }
        ShowSetup(profile);
    }
    private async void ShowSetup(string profile)
    {
        if (ProfileCatalog.Find(profile) is not { } def) return;
        var dialog = new Window
        {
            Title = "Setup — " + def.DisplayName,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        Ui.Paint(dialog);
        var note = new TextBlock { Text = def.SetupHint, TextWrapping = TextWrapping.Wrap };
        var state = new TextBlock { Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        void RefreshState()
        {
            state.Text = IsLaunchable(profile)
                ? def.Executable + " is on PATH."
                : def.Executable + " is not on this process's PATH. If installation changed PATH, reopen Satr and check again.";
        }
        RefreshState();
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(note);
        panel.Children.Add(state);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(Button(Ui.L("Close"), () => dialog.Close()));
        row.Children.Add(Button(Ui.L("Check again"), () =>
        {
            if (IsLaunchable(profile)) { dialog.Close(); AddTab(profile); }
            else RefreshState();
        }));
        panel.Children.Add(row);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }
    private static async Task<string?> ChooseProfile()
    {
        var dialog = new Window { Title = Ui.L("Session type"), Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        Ui.Paint(dialog);
        string? picked = null;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Open in the chosen folder:" });
        foreach (var def in ProfileCatalog.All)
        {
            if (def.FreshId is not null) continue;
            var presence = ProfileCatalog.Presence(def.Id, PtySession.FindExecutable);
            var label = presence == ToolPresence.Installed ? def.PickerLabel : def.PickerLabel + " — Available (setup)";
            panel.Children.Add(Button(label, () => { picked = def.Id; dialog.Close(); }));
        }
        panel.Children.Add(Button(Ui.L("Cancel"), () => dialog.Close()));
        dialog.Content = panel;
        var owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
        return picked;
    }
    private void Send(string text)
    {
        try
        {
            if (_active?.Session is not { } session || _active.Finished) { StatusError("No session ready. Open a shell or an agent."); return; }
            session.Write(text); _active.Follow = true;
        }
        catch (Exception ex) { StatusError(FriendlyPasteError(ex.Message)); }
    }
    private static string FriendlyPasteError(string raw)
    {
        if (raw.Contains("Bracketed", StringComparison.OrdinalIgnoreCase))
            return "This session accepts a single line for now. Open a tool with multi-line paste, or paste one line.";
        if (raw.Contains("1 MB", StringComparison.OrdinalIgnoreCase)) return "Text exceeds 1 MB. Split it into smaller parts.";
        if (raw.Contains("not accepting input", StringComparison.OrdinalIgnoreCase)) return raw + " Wait a moment and retry.";
        return raw;
    }
    private async void CopySelection()
    {
        try
        {
            if (Clipboard is not { } clipboard) return;
            if (!_terminal.HasSelection) { Status("No selection. Drag with the mouse, or Shift+drag while the tool captures the mouse."); return; }
            await clipboard.SetTextAsync(_terminal.GetSelectedText()); Status("Selection copied.");
        }
        catch (Exception ex) { StatusError(ex.Message); }
    }
    private void CopyPath() { if (_active is { } tab) CopyDirectory(tab.Directory); }
    private async void CopyDirectory(string directory)
    {
        try
        {
            if (Clipboard is { } clipboard && !string.IsNullOrEmpty(directory)) { await clipboard.SetTextAsync(directory); Status("Path copied."); }
        }
        catch (Exception ex) { StatusError(ex.Message); }
    }
    private void OpenPath() { OpenDirectory(_active?.Directory ?? _path.Text); }
    private async void OpenDirectory(string? dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) { StatusError("Folder not found."); return; }
            await Launcher.LaunchUriAsync(new Uri(new Uri("file:///"), dir.Replace("\\", "/").TrimEnd('/') + "/"));
        }
        catch (Exception ex) { StatusError(ex.Message); }
    }
    private async void Paste()
    {
        var target = _active;
        try
        {
            var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
            if (target != _active) return;
            if (string.IsNullOrEmpty(text)) { PasteImage(); return; }
            if (text.Length > 2000)
            {
                if (!await Confirm($"Paste {text.Length} characters? Review before sending.", "Paste")) return;
                if (target != _active) return;
            }
            Send(ArabicInput.PreparePaste(text, _active?.Snapshot?.Modes.BracketedPaste == true));
        }
        catch (Exception ex) { StatusError(FriendlyPasteError(ex.Message)); }
    }
    private void ShowTranscript()
    {
        var snapshot = _active?.Buffer.CaptureSnapshot();
        var text = snapshot is null ? "" : string.Join(Environment.NewLine, snapshot.Lines.Select(line => string.Concat(line.Runs.Where(run => !run.Style.Hidden).Select(run => run.Text))));
        if (_active is { Transcript.Length: > 0 } saved) text = "Previous saved output\n" + saved.Transcript + "\n\nCurrent session\n" + text;
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
        var find = new TextBox { PlaceholderText = Ui.L("Filter text…"), Margin = new Thickness(16, 0) };
        find.TextChanged += (_, _) =>
        {
            var q = find.Text ?? "";
            if (q.Length == 0) { box.Text = text; return; }
            box.Text = string.Join(Environment.NewLine, text.Split('\n').Where(l => l.Contains(q, StringComparison.OrdinalIgnoreCase)));
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 8) };
        bar.Children.Add(Button(Ui.L("Copy all"), () => { if (Clipboard is { } cb) cb.SetTextAsync(box.SelectedText.Length > 0 ? box.SelectedText : box.Text); }));
        bar.Children.Add(Button(Ui.L("Wrap text"), () => box.TextWrapping = box.TextWrapping == TextWrapping.Wrap ? TextWrapping.NoWrap : TextWrapping.Wrap));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        layout.Children.Add(find);
        Grid.SetRow(bar, 1); layout.Children.Add(bar);
        Grid.SetRow(box, 2); layout.Children.Add(box);
        layout.Children.Add(new TextBlock { Text = "Tip: Ctrl+Click opens links. Shift+drag selects while captured.", Margin = new Thickness(16, 4), FontSize = 12, Foreground = Ui.Muted });
        Grid.SetRow(layout.Children[^1], 3);
        var window = new Window { Title = "Session text — Satr", Width = 850, Height = 600, Content = layout };
        Ui.Paint(window);
        window.Show(this);
    }
    private void ShowShortcuts() => OpenSettings(3);
    private void ShowAbout() => OpenSettings(4);

    private void GlobalKey(object? sender, KeyEventArgs e)
    {
        if (_searchPanel.IsVisible && e.Key == Key.Escape) { CloseSearch(); e.Handled = true; return; }
        if (_searchPanel.IsVisible && e.Key == Key.F3) { FindText(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; return; }
        if (RunShortcut(e)) { e.Handled = true; return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D1 and <= Key.D8)
        { var index = e.Key - Key.D1; if (index < _tabs.Count) ActivateSession(_tabs[index]); e.Handled = true; }
    }
    private void TerminalKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control); var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_searchPanel.IsVisible && e.Key == Key.Escape) return; // handled by GlobalKey
        if (control && (e.Key is >= Key.A and <= Key.Z || e.Key == Key.Space))
        {
            Send(TerminalKeys.Control(e.Key == Key.Space ? ' ' : (char)('A' + e.Key - Key.A), _active?.Snapshot?.Modes.Win32Input == true)); e.Handled = true; return;
        }
        var sequence = KeySequence(e.Key, e.KeyModifiers, _active?.Snapshot?.Modes.ApplicationCursorKeys == true);
        if (sequence is null && !control && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !string.IsNullOrEmpty(e.KeySymbol))
            sequence = "\x1b" + e.KeySymbol;
        if (sequence is not null)
        {
            if (ImeComposition.BlocksPtyKey(_terminal.IsComposing, control)) return;
            Send(sequence); e.Handled = true;
        }
    }
    internal static string? KeySequence(Key key, KeyModifiers modifiers, bool application)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift); var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (modifiers.HasFlag(KeyModifiers.Control) ? 4 : 0);
        if (key == Key.Tab) return shift ? "\x1b[Z" : "\t";
        var final = key switch { Key.Up => 'A', Key.Down => 'B', Key.Right => 'C', Key.Left => 'D', Key.Home => 'H', Key.End => 'F', _ => '\0' };
        if (final != '\0') return modifier > 1 ? $"\x1b[1;{modifier}{final}" : application ? $"\x1bO{final}" : $"\x1b[{final}";
        var function = key switch { Key.F1 => 'P', Key.F2 => 'Q', Key.F3 => 'R', Key.F4 => 'S', _ => '\0' };
        if (function != '\0') return modifier > 1 ? $"\x1b[1;{modifier}{function}" : $"\x1bO{function}";
        var tilde = key switch { Key.Insert => 2, Key.Delete => 3, Key.PageUp => 5, Key.PageDown => 6, Key.F5 => 15, Key.F6 => 17, Key.F7 => 18, Key.F8 => 19, Key.F9 => 20, Key.F10 => 21, Key.F11 => 23, Key.F12 => 24, _ => 0 };
        if (tilde != 0) return modifier > 1 ? $"\x1b[{tilde};{modifier}~" : $"\x1b[{tilde}~";
        return key switch { Key.Enter => alt ? "\x1b\r" : "\r", Key.Back => alt ? "\x1b\x7f" : "\x7f", Key.Escape => "\x1b", _ => null };
    }
    private bool Mouse(PointerEventArgs e, int button, bool released)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _active?.Snapshot?.Modes is not { MouseTrackingMode: > 0, SgrMouse: true }) return false;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _terminal.IsLinkAt(e)) return false;
        if (!_terminal.TryGetGridCell(e, out var x, out var y)) return false;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) button += 8;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) button += 16;
        Send($"\x1b[<{button};{x};{y}{(released ? 'm' : 'M')}"); e.Handled = true; return true;
    }
    private void MousePressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(_terminal).Properties;
        _terminal.Focus(); Mouse(e, p.IsLeftButtonPressed ? 0 : p.IsMiddleButtonPressed ? 1 : 2, false);
    }
    private void MouseReleased(object? sender, PointerReleasedEventArgs e) => Mouse(e, e.InitialPressMouseButton == MouseButton.Left ? 0 : e.InitialPressMouseButton == MouseButton.Middle ? 1 : 2, true);
    private void MouseMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(_terminal).Properties;
        var mode = _active?.Snapshot?.Modes.MouseTrackingMode;
        if (mode == 1003 || mode == 1002 && (p.IsLeftButtonPressed || p.IsMiddleButtonPressed || p.IsRightButtonPressed))
            Mouse(e, 32 + (p.IsLeftButtonPressed ? 0 : p.IsMiddleButtonPressed ? 1 : p.IsRightButtonPressed ? 2 : 3), false);
    }
    private void MouseWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Mouse(e, e.Delta.Y > 0 ? 64 : 65, false)) return;
        if (_active is { } tab && tab.Follow) { tab.Follow = false; Status("Review mode — scroll down to return to live output."); }
    }
}
