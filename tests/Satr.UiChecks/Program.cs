using System.Reflection;
using Avalonia.Input.Platform;
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
    // TerminalView row geometry is internal to the view; reflection is the intended
    // test seam so no production API is widened just for coverage. The returned
    // cells expose (Start, Length, X, Width, Column, Columns) as public properties.
    private static double RowX(TerminalView view, int row)
    {
        var layout = typeof(TerminalView).GetMethod("Layout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { row })!;
        var cells = (System.Collections.IEnumerable)layout.GetType().GetProperty("Cells")!.GetValue(layout)!;
        return cells.Cast<object>().Select(cell => (double)cell.GetType().GetProperty("X")!.GetValue(cell)!).DefaultIfEmpty(0).Min();
    }
    private static void Shot(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window); using var stream = File.Create(Path.Combine(Output, name + ".png"));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow(workspaceMode: true); desktop.MainWindow = window;
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
                // Ctrl+A must select all in the terminal, intercepted before \x01 reaches the shell.
                var terminalForKeys = (TerminalView)Field(window, "_terminal");
                terminalForKeys.Focus();
                var ctrlA = new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.A, KeyModifiers = Avalonia.Input.KeyModifiers.Control };
                terminalForKeys.RaiseEvent(ctrlA);
                Check(ctrlA.Handled && terminalForKeys.HasSelection, "Ctrl+A must select all before reaching the shell.");
                // While the search box holds focus, Ctrl+A belongs to it, not the terminal.
                Call(window, "OpenSearch");
                var searchBox = (Avalonia.Controls.TextBox)Field(window, "_search");
                searchBox.Text = "abc";
                var searchCtrlA = new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.A, KeyModifiers = Avalonia.Input.KeyModifiers.Control };
                searchBox.RaiseEvent(searchCtrlA);
                // The app must not intercept Ctrl+A while a text box has focus. Avalonia's
                // TextBox applies its own select-all for real key input; a raised routed event
                // bypasses that path, so the observable contract is "not handled by the app".
                Check(!searchCtrlA.Handled, "Ctrl+A must not be stolen from the focused text box.");
                Call(window, "CloseSearch");
                typeof(MainWindow).GetField("_saveTranscripts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                Call(window, "Persist");
                Check(WorkspaceStore.Load(WorkspaceStore.StatePath).Tabs.Any(t => t.Transcript.Contains("Satr UI check")), "Opt-in transcript must persist output.");
                Shot(window, "running-wide");
                window.Width = 680; window.Height = 540;
                await Task.Delay(500);
                Check(((ListBoxItem)list.Items[0]!).Bounds.Height > 0, "Project sessions must remain laid out after resize.");
                Shot(window, "running-narrow");
                // BEL attention: inactive tab + BEL → NeedsAttention; selecting clears it.
                var tabs = (System.Collections.IList)typeof(MainWindow).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var bellTab = tabs.Cast<object>().FirstOrDefault(t => !ReferenceEquals(t, Field(window, "_active")));
                if (bellTab is not null)
                {
                    var tabType = bellTab.GetType();
                    var priorAwaiting = (bool)tabType.GetField("AwaitingLaunch")!.GetValue(bellTab)!;
                    tabType.GetField("AwaitingLaunch")!.SetValue(bellTab, false);
                    tabType.GetField("NeedsAttention")!.SetValue(bellTab, true);
                    typeof(MainWindow).GetMethod("UpdateTab", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new[] { bellTab });
                    var detail = (TextBlock?)tabType.GetField("Detail")!.GetValue(bellTab);
                    Check((detail?.Text ?? "").Contains("attention", StringComparison.OrdinalIgnoreCase),
                        "BEL on inactive tab must set 'Needs attention' in Detail text.");
                    tabType.GetField("AwaitingLaunch")!.SetValue(bellTab, priorAwaiting);
                    var savedActive = Field(window, "_active");
                    typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new[] { bellTab });
                    Check(!(bool)tabType.GetField("NeedsAttention")!.GetValue(bellTab)!,
                        "Selecting the tab must clear NeedsAttention.");
                    typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new[] { savedActive });
                }
                // Smart RTL cursor-row override: an Arabic-base cursor line keeps its
                // terminal-grid columns, while the same paragraph right-aligns when its
                // row is not the visible cursor row.
                var rtlView = (TerminalView)Field(window, "_terminal");
                // A fresh buffer keeps the row contract deterministic and independent of
                // whatever the live session already printed.
                var rtlBuffer = new TerminalBuffer(80, 5);
                var cursorSnapshot = rtlBuffer.Process("مرحبا بالعالم");
                rtlView.Present(cursorSnapshot, smartRtl: true, cellWidth: 8.5, lineHeight: 18, followOutput: false);
                await Task.Delay(200);
                Check(Math.Abs(RowX(rtlView, cursorSnapshot.CursorRow)) < 0.5,
                    "Visible cursor row must stay grid-aligned despite an Arabic-base paragraph.");
                // Arabic on row 0, then move the cursor to row 1 so the Arabic row is no
                // longer the cursor row and the paragraph is free to right-align.
                var nonCursorSnapshot = rtlBuffer.Process("\r\nplain");
                Check(nonCursorSnapshot.CursorRow == 1 && nonCursorSnapshot.Lines.Count > 1,
                    "Arabic row must sit above the cursor row for a valid non-cursor comparison.");
                rtlView.Present(nonCursorSnapshot, smartRtl: true, cellWidth: 8.5, lineHeight: 18, followOutput: false);
                await Task.Delay(200);
                Check(RowX(rtlView, 0) > 0.5,
                    "Arabic-base row must right-align when it is not the visible cursor row.");
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
                // Give a natural exit a chance to raise Ended first: DisposeAsync sets
                // _disposed and the reader then skips Ended, so Finished would never land.
                for (var i = 0; i < 20 && !(bool)Field(active, "Finished"); i++) await Task.Delay(100);
                await ((IAsyncDisposable)Field(active, "Session")).DisposeAsync();
                File.WriteAllText(Path.Combine(Output, "result.txt"), "PASS: restored projects, selection, explicit launch, terminal output, narrow layout, sidebar toggle, PTY cleanup.");
                var projectsBefore = WorkspaceStore.Load(WorkspaceStore.StatePath).Projects!.Length;
                typeof(MainWindow).GetMethod("CloseTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new[] { active, (object)true });
                await Task.Delay(300);
                Check(WorkspaceStore.Load(WorkspaceStore.StatePath).Projects!.Length == projectsBefore, "Closing a session must preserve its project.");
                var defaults = (System.Collections.Generic.Dictionary<string, string>)typeof(MainWindow).GetField("DefaultShortcuts", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
                Check(defaults["Select all"] == "Ctrl+A", "Ctrl+A must select all at the application level.");
                // Shared-UI parity: these actions must exist with the same shortcuts on Windows and Linux.
                Check(defaults.TryGetValue("Copy selection", out var copyGesture) && copyGesture.Length > 0, "Copy must have a shortcut on every platform.");
                Check(defaults.TryGetValue("Paste into terminal", out var pasteGesture) && pasteGesture.Length > 0, "Paste must have a shortcut on every platform.");
                Check(defaults.TryGetValue("Search", out var searchGesture) && searchGesture.Length > 0, "Search must have a shortcut on every platform.");
                var terminalMenu = ((TerminalView)Field(window, "_terminal")).ContextMenu;
                var menuText = terminalMenu is null ? "" : string.Join("\n", terminalMenu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? ""));
                foreach (var action in new[] { "copy", "paste", "select", "search" })
                    Check(menuText.Contains(action, StringComparison.OrdinalIgnoreCase), $"Terminal menu must expose {action} on every platform.");
                // Copy last AI response: Shell sessions refuse; Omp sessions copy the capture file.
                var tabForAi = Field(window, "_active");
                var aiTabType = tabForAi.GetType();
                var profileField = aiTabType.GetField("Profile")!;
                var captureField = aiTabType.GetField("AiCaptureFile")!;
                var origProfile = (string)profileField.GetValue(tabForAi)!;
                var origCapture = (string?)captureField.GetValue(tabForAi);
                var statusBlock = (TextBlock)Field(window, "_status");
                var testCaptureFile = Path.Combine(Path.GetTempPath(), "satr-ai-" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    Call(window, "ClearError", null!, EventArgs.Empty);
                    if (window.Clipboard is { } cb) await cb.SetTextAsync("sentinel-no-copy");
                    profileField.SetValue(tabForAi, "Shell");
                    Call(window, "CopyLastAiResponse");
                    Check(statusBlock.Text == "Only Omp sessions support copying AI responses.",
                        "CopyLastAiResponse on Shell session must report unsupported profile.");
                    if (window.Clipboard is { } cb2)
                        Check(await cb2.TryGetTextAsync() == "sentinel-no-copy",
                            "CopyLastAiResponse on Shell session must not copy to clipboard.");

                    profileField.SetValue(tabForAi, "Omp");

                    // Non-existent capture file:
                    captureField.SetValue(tabForAi, testCaptureFile);
                    if (File.Exists(testCaptureFile)) File.Delete(testCaptureFile);
                    statusBlock.Text = "";
                    Call(window, "CopyLastAiResponse");
                    Check(statusBlock.Text == "No AI response available yet.",
                        "Missing capture file must surface 'No AI response available yet.'");

                    // Empty capture file:
                    File.WriteAllText(testCaptureFile, "");
                    statusBlock.Text = "";
                    Call(window, "CopyLastAiResponse");
                    for (var i = 0; i < 20 && statusBlock.Text != "No AI response available yet."; i++)
                        await Task.Delay(50);
                    Check(statusBlock.Text == "No AI response available yet.",
                        "Empty capture file must surface 'No AI response available yet.'");

                    // Populated capture file:
                    const string expectedAiResponse = "مرحبا بالعالم — AI response test";
                    File.WriteAllText(testCaptureFile, expectedAiResponse);
                    statusBlock.Text = "";
                    Call(window, "CopyLastAiResponse");
                    for (var i = 0; i < 20 && statusBlock.Text != "Last AI response copied."; i++)
                        await Task.Delay(50);
                    Check(statusBlock.Text == "Last AI response copied.",
                        "Populated capture file must surface 'Last AI response copied.'");
                    if (window.Clipboard is { } cb3)
                    {
                        var clipText = await cb3.TryGetTextAsync();
                        Check(clipText == expectedAiResponse, "Clipboard text must match AI capture file.");
                    }
                }
                finally
                {
                    profileField.SetValue(tabForAi, origProfile);
                    captureField.SetValue(tabForAi, origCapture);
                    if (File.Exists(testCaptureFile)) File.Delete(testCaptureFile);
                }
                // Mouse tracking context menu bypass:
                // Normal right-click inside a mouse-tracking TUI is swallowed so the TUI gets the event;
                // Shift + right-click bypasses mouse tracking to open Satr's context menu.
                var activeTab = Field(window, "_active");
                var tabBuffer = (TerminalBuffer)Field(activeTab, "Buffer");
                var tabSnapshotField = activeTab.GetType().GetField("Snapshot")!;
                var origSnapshot = tabSnapshotField.GetValue(activeTab);
                var mouseSnap = tabBuffer.Process("\x1b[?1000h\x1b[?1006h");
                tabSnapshotField.SetValue(activeTab, mouseSnap);
                var termView = (TerminalView)Field(window, "_terminal");
                typeof(MainWindow).GetField("_shiftRightClick", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
                var normalReq = new Avalonia.Input.ContextRequestedEventArgs();
                Call(window, "TerminalContextRequested", termView, normalReq);
                Check(normalReq.Handled, "Normal right-click in mouse-tracking TUI must be swallowed.");
                typeof(MainWindow).GetField("_shiftRightClick", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var shiftReq = new Avalonia.Input.ContextRequestedEventArgs();
                Call(window, "TerminalContextRequested", termView, shiftReq);
                Check(!shiftReq.Handled, "Shift + right-click in mouse-tracking TUI must bypass tracking and allow context menu.");
                Check(!(bool)typeof(MainWindow).GetField("_shiftRightClick", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!, "_shiftRightClick must be consumed and cleared.");
                tabSnapshotField.SetValue(activeTab, origSnapshot);
                tabBuffer.Process("\x1b[?1000l\x1b[?1006l");
                var cleanReq = new Avalonia.Input.ContextRequestedEventArgs();
                Call(window, "TerminalContextRequested", termView, cleanReq);
                Check(!cleanReq.Handled, "Normal right-click outside mouse tracking must not be swallowed.");
                var toggle = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Toggle sidebar");
                Check(toggle is not null, "Sidebar toggle must be available on every platform.");
                Check(((Border)Field(window, "_sidebar")).IsVisible, "Sidebar must be visible by default.");
                Call(window, "OpenSearch");
                Check(((Border)Field(window, "_searchPanel")).IsVisible, "Search must open on every platform.");
                Call(window, "CloseSearch");
                // Chrome flow: without Arabic UI mode the shell window must be LTR.
                Check(window.FlowDirection == Avalonia.Media.FlowDirection.LeftToRight,
                    "Non-Arabic window must have LeftToRight flow direction.");
                // Arabic mode: a window constructed with Ui.Arabic=true must be RTL;
                // its TerminalView and every TextBox must remain LTR.
                var uiType = typeof(MainWindow).Assembly.GetType("Satr.Ui")!;
                var arabicField = uiType.GetField("Arabic", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
                var priorArabic = (bool)arabicField.GetValue(null)!;
                try
                {
                    arabicField.SetValue(null, true);
                    Call(window, "OpenNewWindow");
                    await Task.Delay(1500);
                    var arabicWin = desktop.Windows.OfType<MainWindow>().First(w => !ReferenceEquals(w, window));
                    Check(arabicWin.FlowDirection == Avalonia.Media.FlowDirection.RightToLeft,
                        "Arabic-mode window must have RightToLeft flow direction.");
                    var arabicTerminal = (TerminalView)Field(arabicWin, "_terminal");
                    Check(arabicTerminal.FlowDirection == Avalonia.Media.FlowDirection.LeftToRight,
                        "Terminal inside Arabic-mode window must remain LeftToRight.");
                    var arabicSearch = (Avalonia.Controls.TextBox)Field(arabicWin, "_search");
                    Check(arabicSearch.FlowDirection == Avalonia.Media.FlowDirection.LeftToRight,
                        "Search TextBox inside Arabic-mode window must remain LeftToRight.");
                    var arabicActive = Field(arabicWin, "_active");
                    await ((IAsyncDisposable)Field(arabicActive, "Session")).DisposeAsync();
                    await Task.Delay(200);
                    typeof(MainWindow).GetMethod("CloseTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(arabicWin, new[] { arabicActive, (object)true });
                    await Task.Delay(300);
                    arabicWin.Close();
                    for (var i = 0; i < 20 && desktop.Windows.OfType<MainWindow>().Count() > 1; i++) await Task.Delay(100);
                }
                finally { arabicField.SetValue(null, priorArabic); }
                Call(window, "OpenNewWindow");
                await Task.Delay(1500);
                var satr = desktop.Windows.OfType<MainWindow>().ToArray();
                Check(satr.Length == 2, "A new window must open and stay shown.");
                var second = satr.First(w => !ReferenceEquals(w, window));
                var secondTabs = (System.Collections.IList)Field(second, "_tabs");
                Check(secondTabs.Count == 1, "A new window starts with its own shell session.");
                var secondActive = Field(second, "_active");
                Check(!ReferenceEquals(Field(window, "_active"), secondActive) && Field(secondActive, "Session") is not null, "Windows must not share session state.");
                await ((IAsyncDisposable)Field(secondActive, "Session")).DisposeAsync();
                await Task.Delay(200);
                typeof(MainWindow).GetMethod("CloseTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(second, new[] { secondActive, (object)true });
                await Task.Delay(300);
                second.Close();
                for (var i = 0; i < 20 && desktop.Windows.OfType<MainWindow>().Count() > 1; i++) await Task.Delay(100);
                Check(desktop.Windows.OfType<MainWindow>().Count() == 1, "Closing the second window must keep the first running.");
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
