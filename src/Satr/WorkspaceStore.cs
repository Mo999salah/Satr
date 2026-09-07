using System.IO;
using System.Text;
using System.Text.Json;

namespace Satr;

public sealed record SavedTab(string Profile, string Directory, string Draft, bool RightToLeft, string? Title = null, string? Project = null, string Transcript = "", string? ConversationId = null);
public sealed record SavedProject(string Path, bool Pinned = false, bool Collapsed = false);
public sealed record SavedWorkspace(SavedTab[] Tabs, int SelectedTab, double FontSize = 16, bool SmartRtl = true, string? FontFamily = null,
    double WindowWidth = 1220, double WindowHeight = 820, int? WindowX = null, int? WindowY = null, bool Maximized = false, int ScrollbackRows = 5000, int SchemaVersion = 0, SavedProject[]? Projects = null, bool SidebarHidden = false, double SidebarWidth = 256, bool SaveTranscripts = false, bool ArabicUi = false, Dictionary<string, string>? Shortcuts = null);
public readonly record struct WorkspaceRead(SavedWorkspace Workspace, bool FromBackup);

public static class WorkspaceStore
{
    public const int CurrentSchema = 2;
    private const string CurrentName = "Satr";
    private const string LegacyName = "Satr-Preview";
    private static string BaseDirectory => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        : Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { } xdg && Path.IsPathFullyQualified(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
    public static string DataDirectory => Environment.GetEnvironmentVariable("SATR_DATA_DIR")
        ?? Path.Combine(BaseDirectory, CurrentName);
    public static string StatePath => Path.Combine(DataDirectory, "workspace.json");

    // One-time upgrade from the preview name. Skipped under SATR_DATA_DIR overrides (tests).
    internal static void MigrateLegacyDirectory(string current, string legacy)
    {
        try { if (!Directory.Exists(current) && Directory.Exists(legacy)) Directory.Move(legacy, current); }
        catch { }
    }
    public static void MigrateLegacyDirectory()
    {
        if (Environment.GetEnvironmentVariable("SATR_DATA_DIR") is not null) return;
        MigrateLegacyDirectory(Path.Combine(BaseDirectory, CurrentName), Path.Combine(BaseDirectory, LegacyName));
    }

    public static SavedWorkspace Load(string path)
    {
        if (!File.Exists(path)) return Normalize(new([], 0));
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Restore file exceeds the allowed size.");
        var state = JsonSerializer.Deserialize<SavedWorkspace>(File.ReadAllText(path, Encoding.UTF8));
        if (state?.Tabs is null || state.Tabs.Length > 50 || state.SchemaVersion < 0 || state.Tabs.Any(tab => tab is null ||
            tab.Title?.Length > 100 || tab.Profile is null || tab.Directory is null || tab.Draft is null || tab.Draft.Length > 1024 * 1024 ||
            tab.Project?.Length > 4096 || tab.Transcript is null || tab.Transcript.Length > 262144 || tab.ConversationId is { Length: > 0 } id && !Guid.TryParse(id, out _)))
            throw new InvalidDataException("Restore file is invalid; left untouched.");
        return Normalize(state);
    }

    public static WorkspaceRead LoadRecovering(string path)
    {
        if (!File.Exists(path))
        {
            var backupOnly = path + ".bak";
            return File.Exists(backupOnly)
                ? new(Load(backupOnly), true)
                : new(Normalize(new([], 0)), false);
        }

        try { return new(Load(path), false); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            if (!File.Exists(path + ".bak")) throw;
            var recovered = Load(path + ".bak");
            // Preserve the damaged file for recovery before the next atomic save replaces it.
            File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
            return new(recovered, true);
        }
    }

    public static SavedWorkspace Normalize(SavedWorkspace state)
    {
        if (state.SchemaVersion > CurrentSchema)
            throw new NotSupportedException("This workspace requires a newer Satr version. The file was left untouched.");
        if (state.Projects is { Length: > 200 } || state.Projects?.Any(p => p is null || string.IsNullOrWhiteSpace(p.Path) || p.Path.Length > 4096) == true)
            throw new InvalidDataException("Invalid project list.");
        return state with
        {
            SchemaVersion = CurrentSchema,
            Tabs = [.. state.Tabs.Select(tab => tab with
            {
                Project = string.IsNullOrWhiteSpace(tab.Project) ? tab.Directory : tab.Project
            })]
        };
    }

    public static void Save(string path, SavedWorkspace state)
    {
        state = Normalize(state);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state with { SchemaVersion = CurrentSchema });
        if (bytes.Length > 32 * 1024 * 1024) throw new IOException("Drafts hit the 32 MB save limit. Store large texts in separate files.");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
