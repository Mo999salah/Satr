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
Check(MainWindow.ValidateShortcuts(null)["Select all"] == "Ctrl+A", "Ctrl+A selects all at the application level");
Check(MainWindow.ValidateShortcuts(new() { ["Select all"] = "Ctrl+Shift+A" })["Select all"] == "Ctrl+A", "retired select-all binding follows the new default");
Check(MainWindow.ValidateShortcuts(new() { ["Select all"] = "Ctrl+Alt+A" })["Select all"] == "Ctrl+Alt+A", "custom select-all binding is preserved");
var conversation = "01234567-89ab-cdef-0123-456789abcdef";
Check(ProfileCatalog.ResolveLaunch("Codex", _ => "/opt/codex", conversation).Arguments is ["resume", "01234567-89ab-cdef-0123-456789abcdef"], "exact codex binding");
var extensionPath = OmpCapture.EnsureExtension();
Check(ProfileCatalog.ResolveLaunch("Omp", _ => "/opt/omp", conversation).Arguments is ["--resume", "01234567-89ab-cdef-0123-456789abcdef", "--extension", var ompExt] && ompExt == extensionPath, "exact omp binding");
var ompPlan = ProfileCatalog.ResolveLaunch("Omp", name => name == "omp" ? "/opt/omp" : null);
Check(ompPlan.App == "/opt/omp" && ompPlan.Arguments is ["--extension", var freshExt] && freshExt == extensionPath, "omp fresh launch includes extension");
var ompResumePlan = ProfileCatalog.ResolveLaunch("OmpResume", name => name == "omp" ? "/opt/omp" : null);
Check(ompResumePlan.App == "/opt/omp" && ompResumePlan.Arguments is ["--continue", "--extension", var resumeExt] && resumeExt == extensionPath, "omp resume launch includes continue and extension");
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
    var wrappedOmp = ProfileCatalog.ResolveLaunch("OmpResume", _ => @"C:\tools\omp.cmd");
    Check(wrappedOmp.App.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && wrappedOmp.Arguments is ["/D", "/V:OFF", "/S", "/C", var ompCmd] && ompCmd.Contains("--continue") && ompCmd.Contains("--extension"), "windows wrapper preserves omp arguments");
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
    var scopedExt = OmpCapture.EnsureExtension();
    Check(File.Exists(scopedExt) && Path.GetDirectoryName(scopedExt) == temporary, "omp extension created in active data directory");
    Check(File.ReadAllText(scopedExt, System.Text.Encoding.UTF8) == OmpCapture.Script, "omp extension file content matches capture script");
    File.WriteAllText(scopedExt, "// tampered content");
    Check(OmpCapture.EnsureExtension() == scopedExt, "extension path stable after re-ensure");
    Check(File.ReadAllText(scopedExt, System.Text.Encoding.UTF8) == OmpCapture.Script, "tampered extension file restored by EnsureExtension");
    Check(OmpCapture.Script.Contains("process.env.SATR_AI_CAPTURE_FILE"), "capture script references SATR_AI_CAPTURE_FILE");
    Check(OmpCapture.Script.Contains("agent_end"), "capture script hooks agent_end");
    Check(OmpCapture.Script.Contains("role === \"assistant\""), "capture script inspects assistant role");
    Check(OmpCapture.Script.Contains("type === \"text\""), "capture script extracts text content");
    Check(OmpCapture.Script.Contains("renameSync") && OmpCapture.Script.Contains("writeFileSync"), "capture script writes atomically via temporary file and rename");
    Check(OmpCapture.Script.Contains("process.pid"), "capture script uses collision-safe temp filename");
    var captureFile = Path.Combine(temporary, "capture.txt");
    var testPluginMjs = Path.Combine(temporary, "test-plugin.mjs");
    File.WriteAllText(testPluginMjs, OmpCapture.Script);
    var harnessScript = Path.Combine(temporary, "test-capture.mjs");
    File.WriteAllText(harnessScript, $$"""
        import fs from "node:fs";
        import path from "node:path";
        import plugin from {{System.Text.Json.JsonSerializer.Serialize(testPluginMjs.Replace('\\', '/'))}};
        const listeners = {};
        const pi = { on: (event, handler) => { listeners[event] = handler; } };
        plugin(pi);
        const agentEnd = listeners["agent_end"];
        if (!agentEnd) throw new Error("agent_end not registered");

        // Scenario 1: assistant text + tool call captures only assistant text
        await agentEnd({
          messages: [
            { role: "user", content: [{ type: "text", text: "hi" }] },
            { role: "assistant", content: [
                { type: "text", text: "Assistant response 1" },
                { type: "tool_use", name: "tool1", input: {} }
              ]
            }
          ]
        });
        if (fs.readFileSync(process.env.SATR_AI_CAPTURE_FILE, "utf8") !== "Assistant response 1")
          throw new Error("scenario 1 failed");

        // Scenario 2: trailing tool-call-only assistant message does not replace last valid text response with empty content
        await agentEnd({
          messages: [
            { role: "assistant", content: [{ type: "text", text: "Assistant response 1" }] },
            { role: "tool", content: [{ type: "text", text: "tool result" }] },
            { role: "assistant", content: [{ type: "tool_use", name: "tool2", input: {} }] }
          ]
        });
        if (fs.readFileSync(process.env.SATR_AI_CAPTURE_FILE, "utf8") !== "Assistant response 1")
          throw new Error("scenario 2 failed");

        // Scenario 3: second real assistant text response atomically replaces first
        await agentEnd({
          messages: [
            { role: "assistant", content: [{ type: "text", text: "Assistant response 1" }] },
            { role: "user", content: [{ type: "text", text: "next" }] },
            { role: "assistant", content: [{ type: "text", text: "Assistant response 2" }] }
          ]
        });
        if (fs.readFileSync(process.env.SATR_AI_CAPTURE_FILE, "utf8") !== "Assistant response 2")
          throw new Error("scenario 3 failed");

        // Scenario 4: Arabic/mixed Unicode is exact
        const unicodeText = "مرحبا بالعالم - Hello 123 - اختبار النص العربي";
        await agentEnd({
          messages: [
            { role: "assistant", content: [{ type: "text", text: unicodeText }] }
          ]
        });
        if (fs.readFileSync(process.env.SATR_AI_CAPTURE_FILE, "utf8") !== unicodeText)
          throw new Error("scenario 4 failed");

        // Scenario 5: when renameSync throws, old response is not overwritten, and temp file is unlinked
        const origRename = fs.renameSync;
        fs.renameSync = () => { throw new Error("simulated rename failure"); };
        try {
          await agentEnd({
            messages: [
              { role: "assistant", content: [{ type: "text", text: "should not overwrite" }] }
            ]
          });
        } finally {
          fs.renameSync = origRename;
        }
        if (fs.readFileSync(process.env.SATR_AI_CAPTURE_FILE, "utf8") !== unicodeText)
          throw new Error("scenario 5 failed: old response was overwritten");
        const dirEntries = fs.readdirSync(path.dirname(process.env.SATR_AI_CAPTURE_FILE));
        if (dirEntries.some(f => f.includes(".tmp.")))
          throw new Error("scenario 5 failed: temp file not unlinked");
        """);
    var jsRuntime = PtySession.FindExecutable("node") ?? PtySession.FindExecutable("bun");
    if (jsRuntime is not null)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = jsRuntime,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(harnessScript);
        startInfo.Environment["SATR_AI_CAPTURE_FILE"] = captureFile;
        var jsProc = System.Diagnostics.Process.Start(startInfo)!;
        jsProc.WaitForExit();
        Check(jsProc.ExitCode == 0, $"{Path.GetFileName(jsRuntime)} test harness failed: {jsProc.StandardError.ReadToEnd()}");
        Check(File.ReadAllText(captureFile, System.Text.Encoding.UTF8) == "مرحبا بالعالم - Hello 123 - اختبار النص العربي", "arabic/mixed unicode captured exact");
    }
    var shareTarget = Path.Combine(temporary, "share-test.txt");
    var shareReplacement = Path.Combine(temporary, "share-replacement.txt");
    File.WriteAllText(shareTarget, "original capture payload", System.Text.Encoding.UTF8);
    File.WriteAllText(shareReplacement, "replaced capture payload", System.Text.Encoding.UTF8);
    await using (var readStream = new FileStream(
        shareTarget,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        useAsync: true))
    {
        File.Move(shareReplacement, shareTarget, overwrite: true);
        using var reader1 = new StreamReader(readStream, System.Text.Encoding.UTF8);
        var originalContent = await reader1.ReadToEndAsync();
        Check(originalContent == "original capture payload", "concurrent open stream reads complete original payload after replace");

        await using var newStream = new FileStream(
            shareTarget,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader2 = new StreamReader(newStream, System.Text.Encoding.UTF8);
        var newContent = await reader2.ReadToEndAsync();
        Check(newContent == "replaced capture payload", "subsequent stream reads complete new payload after replace");
    }
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
