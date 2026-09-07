using Avalonia.Input;
using Satr;

// Explicit opt-in checks: never spawn a shell, a GUI, or an AI tool.
static void Check(bool value, string name) { if (!value) throw new Exception(name); }
Check(MainWindow.TabTitle("Shell", "/home/inv/project/") == "\u2068project\u2069 · \u2068Shell\u2069", "project tab title with isolates");
Check(MainWindow.TabTitle("CodexResume", "/").Contains("\u2068/\u2069"), "root directory tab title");
Check(MainWindow.TabTitle("CodexResume", "/work/app").Contains("Codex · resume"), "resumed session label");
Check(MainWindow.TabTitle("AgyResume", "/work/app").Contains("Agy · resume"), "agy resumed session label");
Check(MainWindow.TabTitle("OmpResume", "/work/app").Contains("Omp · resume"), "omp resumed session label");
Check(MainWindow.IsAi("Omp") && !MainWindow.IsAi("Shell") && MainWindow.ToolFor("OmpResume") == "omp", "omp tool mapping");
Check(MainWindow.ResumeOf("Omp") == "OmpResume" && MainWindow.FreshOf("OmpResume") == "Omp", "omp resume mapping");
Check(MainWindow.KeySequence(Key.Up, KeyModifiers.None, true) == "\x1bOA", "application cursor");
Check(MainWindow.KeySequence(Key.Left, KeyModifiers.Control, false) == "\x1b[1;5D", "Ctrl+Left");
Check(MainWindow.KeySequence(Key.F12, KeyModifiers.Shift, false) == "\x1b[24;2~", "Shift+F12");
Check(ArabicInput.PreparePaste("hello\nCodex", true) == "\x1b[200~hello\nCodex\x1b[201~", "logical paste");
try { ArabicInput.PreparePaste("a\nb", false); throw new Exception("unsafe paste accepted"); }
catch (ArgumentException) { }
var temporary = Path.Combine(Path.GetTempPath(), "satr-check-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("SATR_DATA_DIR");
try
{
    Environment.SetEnvironmentVariable("SATR_DATA_DIR", temporary);
    Check(WorkspaceStore.DataDirectory == temporary, "data override");
    var state = new SavedWorkspace([new SavedTab("Codex", temporary, "arabic draft", true)], 0, 19, false);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state with { FontSize = 20 });
    File.WriteAllText(WorkspaceStore.StatePath, "invalid");
    var recovered = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
    Check(recovered.FontSize == 19 && recovered.Tabs[0].Draft == "arabic draft", "atomic backup recovery");
    Check(Directory.GetFiles(temporary, "*.damaged-*").Length == 1, "damaged state retained");
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
