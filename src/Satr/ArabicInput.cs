namespace Satr;

public static class ArabicInput
{
    public static string PreparePaste(string text, bool bracketedPaste)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("النص يحتوي محارف تحكم. احذفها قبل نقله إلى الطرفية.");
        if (!bracketedPaste && text.IndexOfAny(['\r', '\n', '\t']) >= 0)
            throw new ArgumentException("الجلسة لا تدعم اللصق المحمي الآن. انقل سطرًا واحدًا دون Tab، أو افتح أداة تدعم Bracketed Paste أولًا. النص ما زال في المحرر.");
        // Preserve logical Unicode order: shaping belongs only to the renderer.
        return bracketedPaste ? $"\x1b[200~{text.Replace("\r\n", "\n").Replace('\r', '\n')}\x1b[201~" : text;
    }
}
