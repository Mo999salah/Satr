using Avalonia.Input;
using Satr;

// Explicit opt-in checks: never spawn a shell, a GUI, or an AI tool.
static void Check(bool value, string name) { if (!value) throw new Exception(name); }
Check(MainWindow.KeySequence(Key.Up, KeyModifiers.None, true) == "\x1bOA", "application cursor");
Check(MainWindow.KeySequence(Key.Left, KeyModifiers.Control, false) == "\x1b[1;5D", "Ctrl+Left");
Check(MainWindow.KeySequence(Key.F12, KeyModifiers.Shift, false) == "\x1b[24;2~", "Shift+F12");
Check(ArabicInput.PreparePaste("مرحبا\nCodex", true) == "\x1b[200~مرحبا\nCodex\x1b[201~", "logical paste");
try { ArabicInput.PreparePaste("a\nb", false); throw new Exception("unsafe paste accepted"); }
catch (ArgumentException) { }
var temporary = Path.Combine(Path.GetTempPath(), "satr-check-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("SATR_DATA_DIR");
try
{
    Environment.SetEnvironmentVariable("SATR_DATA_DIR", temporary);
    Check(WorkspaceStore.DataDirectory == temporary, "data override");
    var state = new SavedWorkspace([new SavedTab("Codex", temporary, "مسودة عربية", true)], 0, 19, false);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state);
    WorkspaceStore.Save(WorkspaceStore.StatePath, state with { FontSize = 20 });
    File.WriteAllText(WorkspaceStore.StatePath, "invalid");
    var recovered = WorkspaceStore.LoadRecovering(WorkspaceStore.StatePath);
    Check(recovered.FontSize == 19 && recovered.Tabs[0].Draft == "مسودة عربية", "atomic backup recovery");
    Check(Directory.GetFiles(temporary, "*.damaged-*").Length == 1, "damaged state retained");
}
finally
{
    Environment.SetEnvironmentVariable("SATR_DATA_DIR", previous);
    if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
}
Console.WriteLine("Port checks passed. No terminal process or AI request was started.");
