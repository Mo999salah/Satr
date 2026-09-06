using System.Text;
using Unicode.Bidi;

namespace Satr;

internal readonly record struct DirectionalSpan(int Start, int Length, bool IsRightToLeft);

internal static class SmartRtl
{
    public static bool IsRightToLeft(TerminalLine line) => line.ContainsRightToLeft;
    public static bool ShouldRightAlign(TerminalLine line, bool smartRtlEnabled, bool preserveTerminalGrid) =>
        smartRtlEnabled && !preserveTerminalGrid && BaseRightToLeft(string.Concat(line.Runs.Select(run => run.Text)));

    internal static bool ContainsRightToLeft(IReadOnlyList<TerminalRun> runs) =>
        runs.Any(run => run.Text.EnumerateRunes().Any(rune => CharData.BidiClass(rune) is
            BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.RLE or BidiClass.LRE or
            BidiClass.RLO or BidiClass.LRO or BidiClass.RLI or BidiClass.LRI or BidiClass.FSI or BidiClass.PDI));

    public static bool BaseRightToLeft(string text) =>
        InitialInfo.Create(text).Paragraphs.FirstOrDefault()?.Level.IsRtl() == true;

    // Visual runs include UAX #9 isolate/embedding and bracket resolution.
    // Return logical UTF-16 ranges: shaping must never mutate the stored text.
    public static IReadOnlyList<DirectionalSpan> GetDirectionalSpans(string text, bool baseRightToLeft)
    {
        if (text.Length == 0) return [];
        var info = BidiInfo.Create(text, baseRightToLeft ? Level.Rtl() : Level.Ltr());
        var spans = new List<DirectionalSpan>();
        foreach (var paragraph in info.Paragraphs)
        {
            var (levels, runs) = info.VisualRuns(paragraph, paragraph.Range);
            spans.AddRange(runs.Select(run => new DirectionalSpan(run.Start, run.Length, levels[run.Start].IsRtl())));
        }
        return spans;
    }
}
