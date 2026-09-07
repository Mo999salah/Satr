namespace Satr;

public static class ArabicInput
{
    public static string PreparePaste(string text, bool bracketedPaste)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Text contains control characters. Remove them before pasting into the terminal.");
        if (!bracketedPaste && text.IndexOfAny(['\r', '\n', '\t']) >= 0)
            throw new ArgumentException("This session has no guarded paste right now. Paste a single line without Tab, or open a tool with bracketed-paste support first. Nothing was pasted.");
        // Preserve logical Unicode order: shaping belongs only to the renderer.
        return bracketedPaste ? $"\x1b[200~{text.Replace("\r\n", "\n").Replace('\r', '\n')}\x1b[201~" : text;
    }
}

/// <summary>In-progress IME composition. Overlay only; nothing is written to the PTY until commit.</summary>
public sealed class ImeComposition
{
    public string Text { get; private set; } = "";
    public int? Cursor { get; private set; }
    public bool IsActive => Text.Length > 0;

    public void Set(string? text, int? cursor = null)
    {
        Text = string.IsNullOrEmpty(text) ? "" : text;
        Cursor = Text.Length == 0 ? null : Math.Clamp(cursor ?? Text.Length, 0, Text.Length);
    }

    public void Clear() => Set(null);

    public static bool BlocksPtyKey(bool composing, bool controlHeld) =>
        composing && !controlHeld;
}
