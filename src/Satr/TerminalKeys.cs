namespace Satr;

public static class TerminalKeys
{
    public static string Control(char key, bool win32Input)
    {
        var upper = char.ToUpperInvariant(key);
        if (upper != ' ' && upper is not (>= 'A' and <= 'Z')) throw new ArgumentOutOfRangeException(nameof(key));
        var code = upper == ' ' ? 0 : upper - 'A' + 1;
        // Send the Latin shortcut character explicitly: control codes are remapped through the active Arabic keyboard layout by ReadConsoleInput clients.
        var unicode = (int)char.ToLowerInvariant(upper);
        return win32Input
            ? $"\x1b[{(int)upper};0;{unicode};1;8;1_\x1b[{(int)upper};0;{unicode};0;8;1_"
            : ((char)code).ToString();
    }
}
