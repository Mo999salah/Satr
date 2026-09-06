using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Satr;

public sealed class MainWindow : Window
{
    private readonly TerminalView _terminal = new() { Background = Brush.Parse("#0c0c0c"), FontSize = 16,
        FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Cascadia Mono, Consolas, Segoe UI" : "DejaVu Sans Mono, Noto Sans Arabic") };
    private readonly TextBox _composer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        FlowDirection = FlowDirection.RightToLeft, FontSize = 18, MaxLength = 1024 * 1024, PlaceholderText = "اكتب طلبك بالعربية…" };
    private readonly TextBlock _status = new() { Text = "سطر — مساحة عمل عربية", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _path = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl _tabStrip = new() { MinHeight = 42 };
    private readonly Grid _workspace = new();
    private readonly Border _editorPanel;
    private readonly List<Tab> _tabs = [];
    private readonly DispatcherTimer _render = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly CancellationTokenSource _shutdown = new();
    private FileStream? _instanceLock;
    private Tab? _active;
    private bool _loading, _saveEnabled, _closed, _closing, _stacked, _smartRtl = true;
    private double _cellWidth = 9.6, _lineHeight = 24;
    private string _directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private sealed class Tab(string profile, string directory, string draft, bool rtl)
    {
        public string Profile = profile, Directory = directory, Draft = draft;
        public bool Rtl = rtl, Starting, Closed, Finished, Dirty = true, Follow = true;
        public int Columns = 80, Rows = 24;
        public TerminalBuffer Buffer = new(80, 24);
        public PtySession? Session;
        public TerminalSnapshot? Snapshot;
        public long SynchronizedSince;
        public double Offset;
        public ((int Row, int Offset)? Anchor, (int Row, int Offset)? End) Selection;
        public TabItem Header = new();
    }

    public MainWindow()
    {
        Title = "سطر — Satr"; Width = 1220; Height = 820; MinWidth = 680; MinHeight = 540;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Satr/Satr.ico")));
        Background = Brush.Parse("#17191D"); FontFamily = new FontFamily("Segoe UI, Noto Sans Arabic, DejaVu Sans");
        var header = new DockPanel { Margin = new Thickness(16, 12), LastChildFill = true };
        var brand = new TextBlock { Text = "سطر  /  SATR", FontSize = 22, FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(brand, Dock.Left); header.Children.Add(brand);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("مجلد المشروع", async () => await ChooseDirectory()));
        actions.Children.Add(Button("+ طرفية", () => AddTab("Shell")));
        actions.Children.Add(Button("Codex", () => AddTab("Codex")));
        var more = new Button { Content = "المزيد", Padding = new Thickness(12, 8) };
        var menu = new ContextMenu();
        menu.Items.Add(Item("استئناف Codex", () => AddTab("CodexResume")));
        menu.Items.Add(Item("Claude Code", () => AddTab("Claude")));
        menu.Items.Add(Item("نسخ التحديد", CopySelection));
        menu.Items.Add(Item("لصق في الطرفية", Paste));
        menu.Items.Add(Item("عرض النص للقراءة والنسخ", ShowTranscript));
        menu.Items.Add(Item("تكبير الخط", () => ChangeFont(1)));
        menu.Items.Add(Item("تصغير الخط", () => ChangeFont(-1)));
        menu.Items.Add(Item("تبديل عرض RTL الذكي", () => { _smartRtl = !_smartRtl; Redraw(); }));
        menu.Items.Add(Item("عن سطر", ShowAbout));
        more.ContextMenu = menu; more.Click += (_, _) => menu.Open(more); actions.Children.Add(more);
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        header.Children.Add(new Border());

        var editor = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(16) };
        editor.Children.Add(new TextBlock { Text = "محرر الطلب العربي", FontWeight = FontWeight.SemiBold, FontSize = 17,
            FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 12) });
        Grid.SetRow(_composer, 1); editor.Children.Add(_composer);
        var editorActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        editorActions.Children.Add(Button("نقل للطرفية", Transfer));
        editorActions.Children.Add(Button("RTL / LTR", () => _composer.FlowDirection = _composer.FlowDirection == FlowDirection.RightToLeft ? FlowDirection.LeftToRight : FlowDirection.RightToLeft));
        editorActions.Children.Add(Button("حفظ النص", ExportDraft));
        editorActions.Children.Add(Button("فتح نص", ImportDraft));
        Grid.SetRow(editorActions, 2); editor.Children.Add(editorActions);
        var hint = new TextBlock { Text = "Ctrl+Enter ينقل النص. اضغط Enter داخل الطرفية لإرساله.\nCtrl+Shift+E للعودة إلى المحرر.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .65, FlowDirection = FlowDirection.RightToLeft };
        Grid.SetRow(hint, 3); editor.Children.Add(hint);
        _editorPanel = new Border { Background = Brush.Parse("#202329"), Child = editor };
        _workspace.ColumnDefinitions = new ColumnDefinitions("*,340");
        _workspace.Children.Add(_terminal); Grid.SetColumn(_editorPanel, 1); _workspace.Children.Add(_editorPanel);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        root.Children.Add(header);
        _path.Margin = new Thickness(16, 0, 16, 8); Grid.SetRow(_path, 1); root.Children.Add(_path);
        Grid.SetRow(_tabStrip, 2); root.Children.Add(_tabStrip);
        Grid.SetRow(_workspace, 3); root.Children.Add(_workspace);
        _status.Margin = new Thickness(16, 8); _status.FontSize = 12; Grid.SetRow(_status, 4); root.Children.Add(_status);
        Content = root;
        _tabStrip.SelectionChanged += (_, _) => { if (!_loading && _tabStrip.SelectedItem is TabItem { Tag: Tab tab }) Select(tab); };
        _composer.TextChanged += (_, _) => { if (!_loading) { _save.Stop(); _save.Start(); } };
        _save.Tick += (_, _) => Persist();
        _render.Tick += (_, _) => Render();
        _terminal.SizeChanged += (_, _) => ResizeTerminal();
        _terminal.ViewportChanged += ResizeTerminal;
        _terminal.FollowChanged += follow => { if (_active is { } tab) tab.Follow = follow; };
        SizeChanged += (_, _) => ArrangeWorkspace();
        _terminal.TextInput += (_, e) => { if (!string.IsNullOrEmpty(e.Text)) { Send(e.Text); e.Handled = true; } };
        _terminal.AddHandler(KeyDownEvent, TerminalKey, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, GlobalKey, RoutingStrategies.Tunnel);
        _terminal.GotFocus += (_, _) => { if (_active?.Snapshot?.Modes.FocusReporting == true) Send("\x1b[I"); };
        _terminal.LostFocus += (_, _) => { if (_active?.Snapshot?.Modes.FocusReporting == true) Send("\x1b[O"); };
        _terminal.AddHandler(PointerPressedEvent, MousePressed, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerReleasedEvent, MouseReleased, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerMovedEvent, MouseMoved, RoutingStrategies.Tunnel);
        _terminal.AddHandler(PointerWheelChangedEvent, MouseWheel, RoutingStrategies.Tunnel);
        _terminal.LinkRequested += async uri => { try { await Launcher.LaunchUriAsync(uri); } catch (Exception ex) { Status(ex.Message); } };
        Opened += (_, _) => Restore();
        Closing += OnClosing;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 5, 0) };
        button.Click += (_, _) => action(); return button;
    }
    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item;
    }
    private void Status(string text) => _status.Text = text;

    private void Restore()
    {
        try
        {
            System.IO.Directory.CreateDirectory(WorkspaceStore.DataDirectory);
            _instanceLock = new FileStream(Path.Combine(WorkspaceStore.DataDirectory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) { Status("تعذّر فتح مساحة العمل؛ قد تكون مفتوحة في نافذة أخرى. " + ex.Message); _workspace.IsEnabled = false; return; }
        _loading = true;
        try
        {
            var state = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
            _terminal.FontSize = double.IsFinite(state.FontSize) ? Math.Clamp(state.FontSize, 10, 32) : 16;
            _smartRtl = state.SmartRtl;
            foreach (var tab in state.Tabs)
                AddTab(tab.Profile == "Codex" ? "CodexResume" : tab.Profile, tab.Directory, tab.Draft, tab.RightToLeft);
            if (_tabs.Count > 0) _tabStrip.SelectedIndex = Math.Clamp(state.SelectedTab, 0, _tabs.Count - 1);
            _saveEnabled = true;
        }
        catch (Exception ex) { Status("تعذّرت الاستعادة. الحفظ التلقائي متوقف لحماية الملف؛ استخدم حفظ النص. " + ex.Message); }
        finally { _loading = false; }
        if (_tabs.Count == 0) AddTab("Shell");
        else if (_tabStrip.SelectedItem is TabItem { Tag: Tab tab }) Select(tab);
        MeasureFont(); _render.Start();
    }

    private void AddTab(string profile, string? directory = null, string draft = "", bool rtl = true)
    {
        if (_instanceLock is null) return;
        if (_tabs.Count >= 50) { Status("الحد الأقصى 50 تبويبًا."); return; }
        if (profile is not ("Codex" or "CodexResume" or "Claude")) profile = "Shell";
        var tab = new Tab(profile, directory ?? _directory, draft, rtl);
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        label.Children.Add(new TextBlock { Text = profile == "Shell" ? "طرفية" : profile, VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(Button("×", () => CloseTab(tab)));
        tab.Header = new TabItem { Header = label, Tag = tab };
        _tabs.Add(tab); _tabStrip.Items.Add(tab.Header);
        if (!_loading) { _tabStrip.SelectedItem = tab.Header; Select(tab); Persist(); }
    }

    private void SaveActive()
    {
        if (_active is not { } tab) return;
        tab.Draft = _composer.Text ?? ""; tab.Rtl = _composer.FlowDirection == FlowDirection.RightToLeft;
        tab.Offset = _terminal.VerticalOffset; tab.Selection = _terminal.SelectionState;
    }

    private void Select(Tab tab)
    {
        if (ReferenceEquals(_active, tab)) return;
        SaveActive(); _active = tab; _directory = tab.Directory;
        _loading = true; _composer.Text = tab.Draft; _composer.FlowDirection = tab.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; _loading = false;
        _path.Text = tab.Directory; ToolTip.SetTip(_path, "مجلد بدء الجلسة: " + tab.Directory);
        _terminal.Clear(); tab.Dirty = true; Render();
        _terminal.SelectionState = tab.Selection;
        if (!tab.Follow) _terminal.ScrollToVerticalOffset(tab.Offset);
        ResizeTerminal(); _terminal.Focus();
        if (tab.Session is null && !tab.Starting && !tab.Finished) Start(tab);
        Persist();
    }

    private async void Start(Tab tab)
    {
        tab.Starting = true;
        try
        {
            var session = await PtySession.Start(tab.Profile, tab.Directory, tab.Columns, tab.Rows, _shutdown.Token);
            if (_closed || _closing || tab.Closed) { await session.DisposeAsync(); return; }
            tab.Session = session;
            session.Output += text =>
            {
                TerminalSnapshot snapshot;
                lock (tab) { snapshot = tab.Buffer.Process(text); tab.Snapshot = snapshot; tab.Dirty = true; }
                foreach (var response in snapshot.Responses) session.Write(response);
            };
            session.Ended += message => Dispatcher.UIThread.Post(() =>
            {
                tab.Finished = true; if (!tab.Closed && !_closed) Status(message);
            });
            session.ReadOutput();
            session.Resize(tab.Columns, tab.Rows);
            Status("الجلسة جاهزة • " + tab.Profile);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { tab.Finished = true; Status(ex.Message); }
        finally { tab.Starting = false; }
    }

    private bool Persist()
    {
        _save.Stop();
        if (_loading) return true;
        SaveActive();
        if (!_saveEnabled) return _tabs.All(tab => tab.Draft.Length == 0);
        try
        {
            WorkspaceStore.Save(WorkspaceStore.StatePath, new SavedWorkspace(_tabs.Select(tab =>
                new SavedTab(tab.Profile, tab.Directory, tab.Draft, tab.Rtl)).ToArray(), Math.Max(0, _tabs.IndexOf(_active!)), _terminal.FontSize, _smartRtl));
            return true;
        }
        catch (Exception ex) { Status("تعذّر حفظ المسودات؛ احفظ النص في ملف قبل الإغلاق. " + ex.Message); return false; }
    }

    private async void CloseTab(Tab tab)
    {
        SaveActive();
        try
        {
            if (tab.Draft.Length > 0)
            {
                var archive = Path.Combine(WorkspaceStore.DataDirectory, "drafts"); System.IO.Directory.CreateDirectory(archive);
                await File.WriteAllTextAsync(Path.Combine(archive, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".txt"), tab.Draft, Encoding.UTF8);
            }
        }
        catch (Exception ex) { Status("لم يُغلق التبويب لأن أرشفة المسودة فشلت: " + ex.Message); return; }
        tab.Closed = true;
        _loading = true; _tabs.Remove(tab); _tabStrip.Items.Remove(tab.Header); _loading = false;
        if (ReferenceEquals(_active, tab))
        {
            _active = null;
            if (_tabs.Count > 0) { _tabStrip.SelectedItem = _tabs[0].Header; Select(_tabs[0]); }
            else { _terminal.Clear(); _composer.Text = ""; }
        }
        Persist();
        if (tab.Session is { } session) { try { await session.DisposeAsync(); } catch (Exception ex) { Status(ex.Message); } }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true; if (_closing) return;
        if (!Persist()) { Status("تعذّر حفظ المسودة. استخدم «حفظ النص» ثم امسحها من المحرر قبل الإغلاق."); return; }
        _closing = true; _shutdown.Cancel(); _render.Stop(); _save.Stop();
        try { await Task.WhenAll(_tabs.Where(tab => tab.Session is not null).Select(tab => tab.Session!.DisposeAsync().AsTask())); }
        catch (Exception ex) { Status(ex.Message); }
        finally { _instanceLock?.Dispose(); _closed = true; Close(); }
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
    private void ChangeFont(int delta) { _terminal.FontSize = Math.Clamp(_terminal.FontSize + delta, 10, 32); MeasureFont(); }
    private void ResizeTerminal()
    {
        if (_active is not { } tab || _terminal.ViewportWidth <= 0 || _terminal.ViewportHeight <= 0) return;
        var columns = Math.Max(10, (int)(_terminal.ViewportWidth / _cellWidth));
        var rows = Math.Max(5, (int)(_terminal.ViewportHeight / _lineHeight));
        if (tab.Columns == columns && tab.Rows == rows) return;
        tab.Columns = columns; tab.Rows = rows;
        lock (tab) { tab.Snapshot = tab.Buffer.Resize(columns, rows); tab.Dirty = true; }
        try { tab.Session?.Resize(columns, rows); } catch (Exception ex) { Status(ex.Message); }
    }
    private void ArrangeWorkspace()
    {
        var stacked = Bounds.Width < 940; if (_stacked == stacked) return; _stacked = stacked;
        _workspace.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,340");
        _workspace.RowDefinitions = new RowDefinitions(stacked ? "*,240" : "*");
        Grid.SetRow(_editorPanel, stacked ? 1 : 0); Grid.SetColumn(_editorPanel, stacked ? 0 : 1);
    }

    private async Task ChooseDirectory()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "مجلد المشروع", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } directory) { _directory = directory; AddTab("Shell"); }
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private void Send(string text)
    {
        try
        {
            if (_active?.Session is not { } session || _active.Finished) { Status("لا توجد جلسة جاهزة. افتح طرفية أو Codex."); return; }
            session.Write(text); _active.Follow = true;
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private void Transfer()
    {
        try { Send(ArabicInput.PreparePaste(_composer.Text ?? "", _active?.Snapshot?.Modes.BracketedPaste == true)); _terminal.Focus(); }
        catch (Exception ex) { Status(ex.Message); }
    }
    private async void CopySelection()
    {
        try { if (Clipboard is { } clipboard && _terminal.HasSelection) await clipboard.SetTextAsync(_terminal.GetSelectedText()); }
        catch (Exception ex) { Status(ex.Message); }
    }
    private async void Paste()
    {
        var target = _active;
        try
        {
            var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
            if (target != _active || text is null) return;
            Send(ArabicInput.PreparePaste(text, _active?.Snapshot?.Modes.BracketedPaste == true));
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private async void ExportDraft()
    {
        var text = _composer.Text ?? "";
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "حفظ المسودة", SuggestedFileName = "satr-prompt.txt" });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(text);
            Status("تم حفظ نسخة من المسودة.");
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private async void ImportDraft()
    {
        var target = _active;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "فتح نص", AllowMultiple = false });
            if (files.FirstOrDefault() is not { } file) return;
            await using var stream = await file.OpenReadAsync(); using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var chars = new char[1024 * 1024 + 1]; var length = await reader.ReadBlockAsync(chars, 0, chars.Length);
            if (length > 1024 * 1024) throw new IOException("الملف أكبر من الحد المسموح.");
            if (target != _active || target is null) return;
            // Import into a separate tab so an existing unsaved draft is never overwritten.
            AddTab("Shell", target.Directory, new string(chars, 0, length));
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private void ShowTranscript()
    {
        var snapshot = _active?.Buffer.CaptureSnapshot();
        var text = snapshot is null ? "" : string.Join(Environment.NewLine, snapshot.Lines.Select(line => string.Concat(line.Runs.Where(run => !run.Style.Hidden).Select(run => run.Text))));
        var window = new Window { Title = "نص الجلسة — Satr", Width = 850, Height = 600, Content = new TextBox
            { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) } };
        window.Show(this);
    }
    private void ShowAbout() => new Window { Title = "عن سطر", Width = 500, Height = 300,
        Content = new TextBlock { Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft,
            Text = "سطر — Satr 0.2.0 Preview\nMohamad Salah\n\nواجهة Avalonia وجلسات Windows / Linux.\nمحرك الطرفية مشتق من RtlTerminal بترخيص MIT؛ التفاصيل في licenses.\n\nنسخة أولية: تم البناء فقط، ولم يُختبر التشغيل على Linux أو أدوات AI." } }.Show(this);

    private void GlobalKey(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (shift && e.Key == Key.E) { _composer.Focus(); e.Handled = true; }
        else if (shift && e.Key == Key.T) { AddTab("Shell"); e.Handled = true; }
        else if (e.Key == Key.Tab && _tabs.Count > 0) { _tabStrip.SelectedIndex = (_tabStrip.SelectedIndex + (shift ? _tabs.Count - 1 : 1)) % _tabs.Count; e.Handled = true; }
        else if (shift && e.Key == Key.W && _active is { } tab) { CloseTab(tab); e.Handled = true; }
        else if (e.Key == Key.Enter && _composer.IsKeyboardFocusWithin) { Transfer(); e.Handled = true; }
    }
    private void TerminalKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control); var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control && e.Key == Key.C && (shift || _terminal.HasSelection)) { CopySelection(); e.Handled = true; return; }
        if (control && e.Key == Key.V) { Paste(); e.Handled = true; return; }
        if (control && shift && e.Key == Key.A) { _terminal.SelectAll(); e.Handled = true; return; }
        if (control && (e.Key is >= Key.A and <= Key.Z || e.Key == Key.Space))
        {
            Send(TerminalKeys.Control(e.Key == Key.Space ? ' ' : (char)('A' + e.Key - Key.A), _active?.Snapshot?.Modes.Win32Input == true)); e.Handled = true; return;
        }
        var sequence = KeySequence(e.Key, e.KeyModifiers, _active?.Snapshot?.Modes.ApplicationCursorKeys == true);
        if (sequence is null && !control && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !string.IsNullOrEmpty(e.KeySymbol))
            sequence = "\x1b" + e.KeySymbol;
        if (sequence is not null) { Send(sequence); e.Handled = true; }
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
        if (_active is { } tab) tab.Follow = false;
    }
}
