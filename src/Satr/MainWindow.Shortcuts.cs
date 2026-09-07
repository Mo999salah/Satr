using Avalonia.Input;

namespace Satr;

public sealed partial class MainWindow
{
    internal static readonly Dictionary<string, string> DefaultShortcuts = new()
    {
        ["Commands"] = "Ctrl+Shift+P", ["Search"] = "Ctrl+Shift+F", ["Restart"] = "Ctrl+Shift+R",
        ["New shell"] = "Ctrl+Shift+T", ["Close session"] = "Ctrl+Shift+W",
        ["Next session"] = "Ctrl+Tab", ["Previous session"] = "Ctrl+Shift+Tab",
        ["Move session up"] = "Ctrl+Shift+PageUp", ["Move session down"] = "Ctrl+Shift+PageDown",
        ["Previous prompt"] = "Ctrl+Shift+Up", ["Next prompt"] = "Ctrl+Shift+Down",
        ["Copy selection"] = "Ctrl+Shift+C", ["Paste into terminal"] = "Ctrl+V", ["Select all"] = "Ctrl+Shift+A",
        ["Increase font"] = "Ctrl+OemPlus", ["Decrease font"] = "Ctrl+OemMinus"
    };
    private Dictionary<string, string> _shortcuts = new(DefaultShortcuts);

    internal static Dictionary<string, string> ValidateShortcuts(Dictionary<string, string>? saved)
    {
        var result = new Dictionary<string, string>(DefaultShortcuts);
        if (saved is not null)
            foreach (var pair in saved)
            {
                if (!result.ContainsKey(pair.Key)) throw new ArgumentException("Unknown shortcut action.");
                var gesture = KeyGesture.Parse(pair.Value);
                if ((gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
                    throw new ArgumentException("Shortcuts need Ctrl or Alt to preserve terminal typing.");
                if (gesture.KeyModifiers == KeyModifiers.Control && gesture.Key is >= Key.D1 and <= Key.D8)
                    throw new ArgumentException("Ctrl+1…8 is reserved for session selection.");
                result[pair.Key] = gesture.ToString();
            }
        var gestures = result.Values.Select(KeyGesture.Parse).ToArray();
        if (gestures.Distinct().Count() != gestures.Length) throw new ArgumentException("Two actions use the same shortcut.");
        return result;
    }

    private bool RunShortcut(KeyEventArgs e)
    {
        var action = _shortcuts.FirstOrDefault(p => KeyGesture.Parse(p.Value).Matches(e)).Key;
        if (action is null) return false;
        switch (action)
        {
            case "Commands": ShowPalette(); break;
            case "Search": OpenSearch(); break;
            case "Restart": RestartActive(); break;
            case "New shell": AddTab("Shell"); break;
            case "Close session": if (_active is { } tab) CloseTab(tab); break;
            case "Next session": NavigateSession(1); break;
            case "Previous session": NavigateSession(-1); break;
            case "Move session up": MoveTab(-1); break;
            case "Move session down": MoveTab(1); break;
            case "Previous prompt": JumpPrompt(-1); break;
            case "Next prompt": JumpPrompt(1); break;
            case "Copy selection": CopySelection(); break;
            case "Paste into terminal": Paste(); break;
            case "Select all": _terminal.SelectAll(); break;
            case "Increase font": ChangeFont(1); break;
            case "Decrease font": ChangeFont(-1); break;
        }
        return true;
    }
}
