using Avalonia.Input;
using Satr;

// Explicit opt-in checks: never spawn a shell, a GUI, or an AI tool.
static void Check(bool value, string name) { if (!value) throw new Exception(name); }
Check(!StartupOptions.Parse([]).RestoreWorkspace && StartupOptions.Parse([]).Command is null, "default launch is a plain terminal");
Check(StartupOptions.Parse(["--workspace"]).RestoreWorkspace && StartupOptions.Parse(["--workspace"]).Command is null, "workspace launch restores projects only when explicit");
Check(!StartupOptions.Parse(["-e", "echo", "hello"]).RestoreWorkspace && StartupOptions.Parse(["-e", "echo", "hello"]).Command is ["echo", "hello"], "short command option preserves argv without restoring workspace");
Check(!StartupOptions.Parse(["--command", "tool", "--flag"]).RestoreWorkspace && StartupOptions.Parse(["--command", "tool", "--flag"]).Command is ["tool", "--flag"], "long command option preserves argv without restoring workspace");
Check(StartupOptions.Parse(["--help"]).ShowHelp, "help option");
try { StartupOptions.Parse(["--command"]); throw new Exception("empty command accepted"); } catch (ArgumentException) { }
try { StartupOptions.Parse(["--unknown"]); throw new Exception("unknown option accepted"); } catch (ArgumentException) { }
var externalPlan = PtySession.ResolveExternalCommand(["tool", "--flag", "value"], name => name == "tool" ? "/opt/tool" : null);
Check(externalPlan.App == "/opt/tool" && externalPlan.Arguments is ["--flag", "value"], "external command resolves executable and preserves argv");
try { PtySession.ResolveExternalCommand(["missing"], _ => null); throw new Exception("missing external command accepted"); } catch (FileNotFoundException) { }
Check(MainWindow.TabTitle("Shell", "/home/inv/project/") == "\u2068project\u2069 · \u2068Shell\u2069", "project tab title with isolates");
Check(MainWindow.TabTitle("CodexResume", "/").Contains("\u2068/\u2069"), "root directory tab title");
Check(MainWindow.TabTitle("CodexResume", "/work/app").Contains("Codex · resume"), "resumed session label");
Check(MainWindow.TabTitle("AgyResume", "/work/app").Contains("Agy · resume"), "agy resumed session label");
Check(MainWindow.TabTitle("OmpResume", "/work/app").Contains("Omp · resume"), "omp resumed session label");
Check(MainWindow.IsAi("Omp") && !MainWindow.IsAi("Shell") && MainWindow.ToolFor("OmpResume") == "omp", "omp tool mapping");
Check(MainWindow.ResumeOf("Omp") == "OmpResume" && MainWindow.FreshOf("OmpResume") == "Omp", "omp resume mapping");
Check(string.Join(",", ProfileCatalog.All.Select(p => p.Id)) == "Shell,Codex,CodexResume,Claude,Agy,AgyResume,Omp,OmpResume", "catalog ids");
Check(ProfileCatalog.IsKnown("Claude") && !ProfileCatalog.IsKnown("Mystery"), "catalog known set");
Check(MainWindow.IsFreshAi("Claude") && !MainWindow.IsFreshAi("CodexResume") && !MainWindow.IsFreshAi("Shell"), "fresh agent mapping");
Check(ProfileCatalog.TabLabelOf("CodexResume") == "Codex · resume", "catalog tab label");
Check(ProfileCatalog.Presence("Shell", _ => null) == ToolPresence.Installed, "shell is always installed");
Check(ProfileCatalog.Presence("Codex", _ => null) == ToolPresence.Available, "missing tool stays available");
Check(ProfileCatalog.Presence("Codex", name => name == "codex" ? "/opt/codex" : null) == ToolPresence.Installed, "resolved tool is installed");
Check(!ProfileCatalog.IsLaunchable("Codex", _ => null), "missing tool is not launchable");
var availableCaption = ProfileCatalog.MenuCaption(ProfileCatalog.Find("Codex")!.Value, ToolPresence.Available);
Check(availableCaption.Contains("Available", StringComparison.Ordinal) && availableCaption.Contains("setup", StringComparison.OrdinalIgnoreCase), "available menu offers setup");
Check(ProfileCatalog.PaletteCaption(ProfileCatalog.Find("Codex")!.Value, ToolPresence.Installed) == "Session: new Codex", "installed palette label");
var customKeys = MainWindow.ValidateShortcuts(new() { ["Commands"] = "Ctrl+Alt+P" });
Check(customKeys["Commands"].Contains("Alt"), "custom shortcut applied");
try { MainWindow.ValidateShortcuts(new() { ["Commands"] = "A" }); throw new Exception("typing shortcut accepted"); } catch (ArgumentException) { }
try { MainWindow.ValidateShortcuts(new() { ["Commands"] = "Ctrl+Shift+F" }); throw new Exception("duplicate shortcut accepted"); } catch (ArgumentException) { }
var conversation = "01234567-89ab-cdef-0123-456789abcdef";
Check(ProfileCatalog.ResolveLaunch("Codex", _ => "/opt/codex", conversation).Arguments is ["resume", "01234567-89ab-cdef-0123-456789abcdef"], "exact codex binding");
Check(ProfileCatalog.ResolveLaunch("Omp", _ => "/opt/omp", conversation).Arguments is ["--resume", "01234567-89ab-cdef-0123-456789abcdef"], "exact omp binding");
try { ProfileCatalog.ResolveLaunch("Codex", _ => "/opt/codex", "--bad & command"); throw new Exception("invalid ID accepted"); } catch (ArgumentException) { }
var threw = false;
try { ProfileCatalog.ResolveLaunch("Claude", _ => null); } catch (FileNotFoundException) { threw = true; }
Check(threw, "launch plan requires PATH");
var resumePlan = ProfileCatalog.ResolveLaunch("CodexResume", name => name == "codex" ? "/opt/codex" : null);
Check(resumePlan.App == "/opt/codex" && resumePlan.Arguments is ["resume"], "resume args come from catalog");
if (OperatingSystem.IsWindows())
{
    var wrapped = ProfileCatalog.ResolveLaunch("AgyResume", _ => @"C:\tools\agy.cmd");
    Check(wrapped.App.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && wrapped.Arguments is ["/D", "/V:OFF", "/S", "/C", "\"\"C:\\tools\\agy.cmd\" --continue\""], "windows wrapper preserves resolved path");
}
Check(MainWindow.ResolveProfile("Mystery", restoring: true) == "Mystery", "restore keeps unknown profiles");
Check(MainWindow.ResolveProfile("Mystery", restoring: false) == "Shell", "interactive unknown profiles become shell");
Check(MainWindow.ShouldCreateTab("Codex", restoring: true, available: false), "restore keeps unavailable AI tabs");
Check(!MainWindow.ShouldCreateTab("Codex", restoring: false, available: false), "new session still requires the tool");
Check(MainWindow.ShouldAutoStart("Shell", true) && !MainWindow.ShouldAutoStart("Codex", false), "missing tools do not auto-start");
Check(!MainWindow.ShouldAutoStart("Shell", true, restored: true), "restored tabs wait for an explicit launch");
Check(MainWindow.ShouldOpenPlainShell(null) && !MainWindow.ShouldOpenPlainShell(["htop"]), "plain shell is not added beside an external command");
Check(MainWindow.KeySequence(Key.Up, KeyModifiers.None, true) == "\x1bOA", "application cursor");
Check(MainWindow.KeySequence(Key.Left, KeyModifiers.Control, false) == "\x1b[1;5D", "Ctrl+Left");
Check(MainWindow.KeySequence(Key.F12, KeyModifiers.Shift, false) == "\x1b[24;2~", "Shift+F12");
Check(ArabicInput.PreparePaste("hello\nCodex", true) == "\x1b[200~hello\nCodex\x1b[201~", "logical paste");
try { ArabicInput.PreparePaste("a\nb", false); throw new Exception("unsafe paste accepted"); }
catch (ArgumentException) { }
Check(ImeComposition.BlocksPtyKey(true, false) && !ImeComposition.BlocksPtyKey(true, true), "IME holds keys except Ctrl");
var bounded = System.Diagnostics.Stopwatch.StartNew();
try { PtySession.AwaitBounded(Task.Delay(TimeSpan.FromSeconds(30)), TimeSpan.FromMilliseconds(80)).GetAwaiter().GetResult(); throw new Exception("timeout was hidden"); }
catch (TimeoutException) { }
Check(bounded.Elapsed < TimeSpan.FromSeconds(3), "PTY dispose wait stayed unbounded");
try { ProfileCatalog.ResolveLaunch("Mystery", _ => throw new Exception("unknown profile probed PATH")); throw new Exception("unknown profile launched"); }
catch (NotSupportedException) { }
var temporary = Path.Combine(Path.GetTempPath(), "satr-check-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("SATR_DATA_DIR");
try
{
    Environment.SetEnvironmentVariable("SATR_DATA_DIR", temporary);
    Check(WorkspaceStore.DataDirectory == temporary, "data override");
    var preferences = new SavedWorkspace([new SavedTab("Codex", temporary, "", true, Transcript: "مرحبا", ConversationId: conversation)], 0,
        Projects: [new SavedProject(temporary, true, true)], SidebarHidden: true, SidebarWidth: 310, SaveTranscripts: true);
    WorkspaceStore.Save(WorkspaceStore.StatePath, preferences);
    var roundTrip = WorkspaceStore.Load(WorkspaceStore.StatePath);
    Check(roundTrip.Projects is [{ Pinned: true, Collapsed: true }] && roundTrip.SidebarHidden && roundTrip.SidebarWidth == 310 && roundTrip.Tabs[0].Transcript == "مرحبا" && roundTrip.Tabs[0].ConversationId == conversation, "workspace preferences and transcript roundtrip");
    var state = new SavedWorkspace([new SavedTab("Codex", temporary, "arabic draft", true)], 0, 19, false);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state with { FontSize = 20 });
    File.WriteAllText(WorkspaceStore.StatePath, "invalid");
    var recovered = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
    Check(recovered.FromBackup && recovered.Workspace.FontSize == 19 && recovered.Workspace.Tabs[0].Draft == "arabic draft", "atomic backup recovery");
    Check(recovered.Workspace.Tabs[0].Project == temporary, "legacy tabs fill project from directory");
    Check(recovered.Workspace.SchemaVersion == WorkspaceStore.CurrentSchema, "schema version is filled on load");
    Check(Directory.GetFiles(temporary, "*.damaged-*").Length == 1, "damaged state retained");
    File.WriteAllText(WorkspaceStore.StatePath, """{"Tabs":null}""");
    var invalidRecovered = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
    Check(invalidRecovered.FromBackup && invalidRecovered.Workspace.FontSize == 19 && invalidRecovered.Workspace.Tabs[0].Draft == "arabic draft", "invalid schema uses backup");
    File.Delete(WorkspaceStore.StatePath);
    var missingPrimary = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
    Check(missingPrimary.FromBackup && missingPrimary.Workspace.Tabs[0].Draft == "arabic draft", "missing primary uses backup");
    const string future = """{"SchemaVersion":99,"Tabs":[],"SelectedTab":0,"FutureData":"keep"}""";
    File.WriteAllText(WorkspaceStore.StatePath, future);
    try { WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath); throw new Exception("future schema accepted or replaced by backup"); }
    catch (NotSupportedException) { }
    Check(File.ReadAllText(WorkspaceStore.StatePath) == future, "future workspace changed");
    try { WorkspaceStore.Save(WorkspaceStore.StatePath, state with { SchemaVersion = 99 }); throw new Exception("future schema saved"); }
    catch (NotSupportedException) { }
    File.WriteAllText(WorkspaceStore.StatePath, """{"Tabs":[{"Profile":"Shell","Directory":"/work/app","Draft":"","RightToLeft":true}],"SelectedTab":0}""");
    var legacy = WorkspaceStore.Load(WorkspaceStore.StatePath);
    Check(legacy.SchemaVersion == WorkspaceStore.CurrentSchema && legacy.Tabs[0].Project == "/work/app", "unversioned json fills schema and project");
    var projectDir = Path.Combine(temporary, "project"); var nested = Path.Combine(projectDir, "src");
    Directory.CreateDirectory(nested);
    Check(MainWindow.LaunchDirectory("Shell", projectDir, nested) == nested, "shell launches in session cwd");
    Check(MainWindow.LaunchDirectory("Codex", projectDir, nested) == projectDir, "AI launches in project root");
    Check(MainWindow.LaunchDirectory("Shell", projectDir, Path.Combine(temporary, "gone")) == projectDir, "missing cwd falls back to project");
}
finally
{
    Environment.SetEnvironmentVariable("SATR_DATA_DIR", previous);
    if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
}
Console.WriteLine("Port checks passed. No terminal process or AI request was started.");
var migrateRoot = Path.Combine(Path.GetTempPath(), "satr-migrate-" + Guid.NewGuid().ToString("N"));
try
{
    var legacy = Path.Combine(migrateRoot, "legacy"); var current = Path.Combine(migrateRoot, "current");
    Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "workspace.json"), "{}");
    WorkspaceStore.MigrateLegacyDirectory(current, legacy);
    Check(Directory.Exists(current) && !Directory.Exists(legacy), "legacy workspace migration");
    WorkspaceStore.MigrateLegacyDirectory(current, legacy);
    Check(Directory.Exists(current), "migration idempotent");
}
finally { if (Directory.Exists(migrateRoot)) Directory.Delete(migrateRoot, true); }
Console.WriteLine("Migration checks passed.");
