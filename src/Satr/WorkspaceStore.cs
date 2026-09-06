using System.IO;
using System.Text;
using System.Text.Json;

namespace Satr;

public sealed record SavedTab(string Profile, string Directory, string Draft, bool RightToLeft);
public sealed record SavedWorkspace(SavedTab[] Tabs, int SelectedTab, double FontSize = 16, bool SmartRtl = true);

public static class WorkspaceStore
{
    public static string DataDirectory => Environment.GetEnvironmentVariable("SATR_DATA_DIR")
        ?? Path.Combine(OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { } xdg && Path.IsPathFullyQualified(xdg)
                ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"), "Satr-Preview");
    public static string StatePath => Path.Combine(DataDirectory, "workspace.json");

    public static SavedWorkspace Load(string path)
    {
        if (!File.Exists(path)) return new([], 0);
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("ملف الاستعادة أكبر من الحد المسموح.");
        var state = JsonSerializer.Deserialize<SavedWorkspace>(File.ReadAllText(path, Encoding.UTF8));
        if (state?.Tabs is null || state.Tabs.Length > 50 || state.Tabs.Any(tab => tab is null ||
            tab.Profile is null || tab.Directory is null || tab.Draft is null || tab.Draft.Length > 1024 * 1024))
            throw new InvalidDataException("ملف الاستعادة غير صالح؛ لم يتم تغييره.");
        return state;
    }

    public static SavedWorkspace LoadRecovering(string path)
    {
        try { return Load(path); }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            if (!File.Exists(path + ".bak")) throw;
            var recovered = Load(path + ".bak");
            // Preserve the damaged file for recovery before the next atomic save replaces it.
            File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
            return recovered;
        }
    }

    public static void Save(string path, SavedWorkspace state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        if (bytes.Length > 32 * 1024 * 1024) throw new IOException("وصلت المسودات إلى حد الحفظ 32 MB. احفظ النصوص الكبيرة في ملفات مستقلة.");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
