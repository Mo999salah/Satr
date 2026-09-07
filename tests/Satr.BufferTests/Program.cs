using System.Text;
using Satr;

if (args.Contains("--bidi-conformance")) return BidiConformance.Run(args[^1]);
var tests = new (string Name, Action Run)[]
{
    ("alternate screen is isolated and restored", AlternateScreenIsIsolated),
    ("styled trailing spaces remain visible", StyledTrailingSpacesRemainVisible),
    ("modern SGR attributes reset independently", SgrAttributesResetIndependently),
    ("colon truecolor is parsed", ColonTrueColorIsParsed),
    ("terminal capability queries receive replies", CapabilityQueriesReceiveReplies),
    ("OpenTUI capability handshake does not leak", OpenTuiHandshakeDoesNotLeak),
    ("modern TUI modes are tracked", ModernTuiModesAreTracked),
    ("emoji grapheme clusters keep terminal width", EmojiClustersKeepWidth),
    ("Persian text remains detectable for Smart RTL", PersianTextRemainsSmartRtl),
    ("carriage return is immediate across reads", CarriageReturnIsImmediate),
    ("style changes preserve pending autowrap", StylePreservesWrap),
    ("status redraw clears old characters across reads", StatusRedraw),
    ("wide characters can be erased and overwritten", WideErase),
    ("Kitty keyboard flags do not restore the cursor", KittyKeyboardFlagsDoNotRestoreCursor),
    ("OSC 8 hyperlinks attach to cells", Osc8HyperlinkAttachesToCells),
    ("OSC 133 records prompt rows", Osc133RecordsPromptRows),
    ("leading bidi isolate survives ingestion", LeadingBidiIsolateSurvives),
    ("Arabic ZWJ does not collapse the next letter", ArabicZwjDoesNotCollapseFollowingLetter),
    ("regional indicators survive SGR", RegionalIndicatorsSurviveSgr),
    ("unpaired UTF-16 surrogate does not throw", UnpairedSurrogateDoesNotThrow),
    ("alternate screen clears pending wrap", AlternateScreenClearsPendingWrap),
    ("CAN recovers an unfinished OSC", CanRecoversUnfinishedOsc),
    ("SGR colon subparameters are not independent codes", SgrColonSubparametersAreNotIndependentCodes),
    ("OSC 133 marks follow main-screen reflow", Osc133MarksFollowReflow),
    ("selection remaps across reflow", SelectionRemapsAcrossReflow),
    ("XTVERSION matches the shipped product version", XtversionMatchesProductVersion),
    ("IME composition is overlay-only until cleared", ImeCompositionIsOverlayOnly),
    ("UTF-8 splits of Arabic and emoji survive", Utf8SplitsSurvive),
    ("mixed Unicode survives every read boundary", MixedReadBoundaries),
    ("ESC restarts interrupted control sequences", InterruptedControls),
    ("underline off and actual cell metrics", UnderlineAndMetrics)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL  {test.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
    Console.Error.WriteLine(failure);

return failures.Count == 0 ? 0 : 1;

static void AlternateScreenIsIsolated()
{
    var buffer = new TerminalBuffer(24, 6);
    buffer.Process("shell history\r\nsecond line");

    var alternate = buffer.Process("\x1b[?1049hOpenCode");
    Assert(alternate.Modes.AlternateScreen, "alternate mode was not enabled");
    Assert(alternate.ScrollbackCount == 0, "main scrollback leaked into the TUI");
    Assert(alternate.Lines.Count == 6, "alternate screen did not retain its fixed grid");
    Assert(Text(alternate).Contains("OpenCode"), "alternate content is missing");
    Assert(!Text(alternate).Contains("shell history"), "main screen leaked into alternate content");

    var restored = buffer.Process("\x1b[?1049l");
    Assert(!restored.Modes.AlternateScreen, "alternate mode was not disabled");
    Assert(Text(restored).Contains("shell history"), "main screen was not restored");
}

static void StyledTrailingSpacesRemainVisible()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process("\x1b[48;2;33;35;55m    \x1b[0m");
    var line = snapshot.Lines[0];
    Assert(line.CellLength >= 4, "background cells were trimmed");
    Assert(line.Runs.Any(run =>
        run.Text.Length >= 4 &&
        run.Style.Background == new TerminalColor(33, 35, 55)),
        "background style on blank cells was lost");
}

static void SgrAttributesResetIndependently()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process(
        "\x1b[4;7;9mA\x1b[24;27;29mB");
    var runs = snapshot.Lines[0].Runs;
    var first = runs.Single(run => run.Text == "A").Style;
    var second = runs.Single(run => run.Text.StartsWith('B')).Style;
    Assert(first.Underline && first.Inverse && first.Strikethrough,
        "enabled SGR attributes were not retained");
    Assert(!second.Underline && !second.Inverse && !second.Strikethrough,
        "SGR reset codes leaked into following text");
}

static void ColonTrueColorIsParsed()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process("\x1b[38:2::91:145:255mX");
    var style = snapshot.Lines[0].Runs[0].Style;
    Assert(style.Foreground == new TerminalColor(91, 145, 255),
        "colon-form RGB foreground was parsed incorrectly");
}

static void CapabilityQueriesReceiveReplies()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process("\x1b[6n\x1b[c\x1b]11;?\x07");
    Assert(snapshot.Responses.Any(response => response.EndsWith("R")),
        "cursor-position report is missing");
    Assert(snapshot.Responses.Any(response => response.EndsWith("c")),
        "device-attributes report is missing");
    Assert(snapshot.Responses.Any(response => response.StartsWith("\x1b]11;rgb:")),
        "terminal-background report is missing");
}

static void OpenTuiHandshakeDoesNotLeak()
{
    var buffer = new TerminalBuffer(30, 6);
    var snapshot = buffer.Process(
        "\x1b]10;?\x1b\\" +
        "\x1b]11;?\x1b\\" +
        "\x1b[>0q" +
        "\x1bP+q4d73\x1b\\" +
        "\x1b[?1016$p\x1b[?2027$p\x1b[?2031$p" +
        "\x1b[?1004$p\x1b[?2004$p\x1b[?2026$p" +
        "\x1b[?u" +
        "\x1b]99;i=opentui-notifications:p=?;\x1b\\" +
        "\x1b]1337;Capabilities\x1b\\" +
        "\x1b_Gi=31337,s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\" +
        "\x1b[>4;1mX");
    var visibleText = Text(snapshot);
    var xStyle = snapshot.Lines[0].Runs
        .Single(run => run.Text.Contains('X')).Style;

    Assert(!visibleText.Contains("4d73") &&
        !visibleText.Contains("Gi=31337") &&
        !visibleText.Contains("Capabilities"),
        "a terminal control string leaked into visible output");
    Assert(!xStyle.Bold && !xStyle.Underline,
        "modifyOtherKeys was misread as a graphic rendition");
    Assert(snapshot.Responses.Any(response => response.Contains("Satr")),
        "XTVERSION response is missing");
    Assert(snapshot.Responses.Any(response => response.Contains("0+r4d73")),
        "XTGETTCAP response is missing");
    Assert(snapshot.Responses.Count(response => response.EndsWith("$y")) == 6,
        "not every OpenTUI mode query received a response");
}

static void ModernTuiModesAreTracked()
{
    var buffer = new TerminalBuffer(20, 5);
    var active = buffer.Process(
        "\x1b[?1h\x1b[?2004h\x1b[?1002h\x1b[?1006h\x1b[?2026h");
    Assert(active.Modes.ApplicationCursorKeys, "application cursor mode is missing");
    Assert(active.Modes.BracketedPaste, "bracketed paste mode is missing");
    Assert(active.Modes.MouseTrackingMode == 1002 && active.Modes.SgrMouse,
        "SGR mouse mode is missing");
    Assert(active.Modes.SynchronizedOutput, "synchronized output mode is missing");

    var inactive = buffer.Process(
        "\x1b[?1l\x1b[?2004l\x1b[?1002l\x1b[?1006l\x1b[?2026l");
    Assert(!inactive.Modes.ApplicationCursorKeys &&
        !inactive.Modes.BracketedPaste &&
        inactive.Modes.MouseTrackingMode == 0 &&
        !inactive.Modes.SgrMouse &&
        !inactive.Modes.SynchronizedOutput,
        "TUI modes did not reset cleanly");
}

static void EmojiClustersKeepWidth()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process("👨‍💻🇮🇷X");
    var line = snapshot.Lines[0];
    Assert(line.CellLength == 6,
        $"emoji clusters plus cursor occupied {line.CellLength} cells instead of 6");
    Assert(string.Concat(line.Runs.Select(run => run.Text)) == "👨‍💻🇮🇷X ",
        "emoji cluster text was split or lost");
}

static void PersianTextRemainsSmartRtl()
{
    var buffer = new TerminalBuffer(40, 5);
    var snapshot = buffer.Process("status: سلام دنیا 123");
    var line = snapshot.Lines[0];
    Assert(SmartRtl.IsRightToLeft(line), "Persian content was not detected");
    Assert(!SmartRtl.ShouldRightAlign(
            line,
            smartRtlEnabled: true,
            preserveTerminalGrid: false),
        "English-first terminal prompt unexpectedly moved to the right");
    Assert(!SmartRtl.ShouldRightAlign(
            line,
            smartRtlEnabled: true,
            preserveTerminalGrid: true),
        "full-screen TUI grid would be moved by Smart RTL");
    var text = string.Concat(line.Runs.Select(run => run.Text));
    var spans = SmartRtl.GetDirectionalSpans(text, true);
    Assert(spans.Any(span => span.IsRightToLeft) &&
        spans.Any(span => !span.IsRightToLeft),
        "mixed Persian/Latin direction spans were not preserved");
    var originSpans = SmartRtl.GetDirectionalSpans(text, false);
    Assert(originSpans.Any(span => span.IsRightToLeft) &&
        originSpans.Any(span => !span.IsRightToLeft),
        "LTR-origin mixed line lost RTL spans");
}

static void CarriageReturnIsImmediate()
{
    var buffer = new TerminalBuffer(30, 6);
    var snapshot = buffer.Process("Working\r");
    Assert(snapshot.CursorColumn == 0, "CR left the snapshot cursor at the old column");
    Assert(Text(buffer.Process("\x1b[KDone")).TrimEnd() == "Done", "CR + erase left old text");
}

static void StylePreservesWrap()
{
    var buffer = new TerminalBuffer(10, 6);
    var snapshot = buffer.Process("1234567890\x1b[31mW");
    Assert(string.Concat(snapshot.Lines[0].Runs.Select(run => run.Text)) == "1234567890",
        "SGR cancelled pending wrap and overwrote the last column");
    Assert(snapshot.CursorRow == 1 && snapshot.CursorColumn == 1, "cursor did not wrap");
}

static void StatusRedraw()
{
    const string stream = "Working (0s - esc to interrupt)\r\nold footer" +
        "\x1b[1A\r\x1b[2KWorking 3\x1b[1B\r\x1b[2Knew footer";
    var expected = new TerminalBuffer(40, 6).Process(stream);
    for (var split = 0; split <= stream.Length; split++)
    {
        var buffer = new TerminalBuffer(40, 6);
        buffer.Process(stream[..split]);
        var actual = buffer.Process(stream[split..]);
        Assert(Text(actual) == Text(expected), $"read boundary {split} changed redraw");
    }
    Assert(Text(expected).TrimEnd() == "Working 3\nnew footer", "old status or footer survived redraw");
    var shortUpdate = new TerminalBuffer(30, 6);
    shortUpdate.Process("WWorking\r\x1b[P");
    Assert(Text(shortUpdate.CaptureSnapshot()).TrimEnd() == "Working", "DCH left duplicate W");
}

static void KittyKeyboardFlagsDoNotRestoreCursor()
{
    var buffer = new TerminalBuffer(40, 6);
    buffer.Process("~/project main\r\n> this a test");
    // Fish 4 sends CSI =0u / CSI =29u around every Backspace. Those are
    // Kitty keyboard flags, not SCO restore-cursor (CSI u).
    var snapshot = buffer.Process("\x1b[=0u\x1b[=29u\b\x1b[K\r\x1b[12C");
    Assert(Text(snapshot).Contains("~/project main"),
        "Kitty keyboard flags restored the cursor onto the prompt line and erased it");
    Assert(snapshot.CursorRow == 1,
        $"cursor jumped to row {snapshot.CursorRow} instead of staying on the input line");
}

static void WideErase()
{
    var buffer = new TerminalBuffer(30, 6);
    buffer.Process("👨‍💻Working\r\x1b[2X");
    Assert(Text(buffer.CaptureSnapshot()).TrimEnd() == "  Working", "wide glyph was not erased");
    Assert(Text(buffer.Process("\rOK\x1b[K")).TrimEnd() == "OK", "wide redraw left stale text");
}

static void Osc8HyperlinkAttachesToCells()
{
    var buffer = new TerminalBuffer(40, 6);
    var linked = buffer.Process("\x1b]8;;https://example.com\u0007click\x1b]8;;\u0007 plain");
    var click = linked.Lines[0].Runs.First(run => run.Text.Contains("click", StringComparison.Ordinal));
    Assert(click.Style.Hyperlink is not null &&
        click.Style.Hyperlink.StartsWith("https://example.com", StringComparison.Ordinal),
        "OSC 8 did not attach to written cells");
    var plain = linked.Lines[0].Runs.First(run => run.Text.Contains("plain", StringComparison.Ordinal));
    Assert(plain.Style.Hyperlink is null, "closed OSC 8 still tagged later text");

    var reset = buffer.Process("\r\n\x1b]8;;https://example.com\u0007\x1b[31mred\x1b[0mstill\x1b]8;;\u0007");
    var still = reset.Lines[1].Runs.First(run => run.Text.Contains("still", StringComparison.Ordinal));
    Assert(still.Style.Hyperlink is not null, "SGR 0 cleared OSC 8 hyperlink");

    var blocked = new TerminalBuffer(40, 4).Process("\x1b]8;;javascript:alert(1)\u0007x\x1b]8;;\u0007");
    Assert(blocked.Lines[0].Runs.All(run => run.Style.Hyperlink is null),
        "javascript: OSC 8 was accepted");
}

static void Osc133RecordsPromptRows()
{
    var buffer = new TerminalBuffer(40, 8);
    buffer.Process("\x1b]133;A\u0007first\r\n");
    buffer.Process("output\r\n");
    var snapshot = buffer.Process("\x1b]133;A;click_events=1\u0007second");
    Assert(snapshot.PromptRows.Count >= 2, $"expected two prompt marks, got {snapshot.PromptRows.Count}");
    Assert(snapshot.PromptRows[0] == 0, "first OSC 133 A was not row 0");
    Assert(snapshot.PromptRows[^1] > snapshot.PromptRows[0], "second OSC 133 A did not advance");
}

static void LeadingBidiIsolateSurvives()
{
    var snapshot = new TerminalBuffer(20, 5).Process("\u2067abc\u2069");
    Assert(LineText(snapshot.Lines[0]).Contains('\u2067'),
        "leading RLI was discarded before bidi");
    Assert(LineText(snapshot.Lines[0]).Contains('\u2069'),
        "PDI was discarded");
}

static void MixedReadBoundaries()
{
    foreach (var (text, columns) in new[] { ("\u2067abc\u2069", 3), ("界\u200d界X", 5), ("ب\u200dتX", 3), ("👨\u200d💻X", 3), ("🇮\x1b[31m🇷X", 3) })
    {
        var expected = new TerminalBuffer(20, 5).Process(text);
        for (var split = 0; split <= text.Length; split++)
        {
            var buffer = new TerminalBuffer(20, 5);
            buffer.Process(text[..split]);
            buffer.CaptureSnapshot();
            var actual = buffer.Process(text[split..]);
            Assert(Text(actual) == Text(expected), $"read boundary {split} changed {text}");
            Assert(actual.CursorColumn == columns, $"wrong width for {text}: {actual.CursorColumn}");
        }
    }
}

static void InterruptedControls()
{
    foreach (var prefix in new[] { "\x1b[31", "\x1b]9;partial", "\x1bPpartial", "\x1b_partial" })
    {
        var buffer = new TerminalBuffer(20, 5);
        buffer.Process(prefix);
        Assert(Text(buffer.Process("\x1b[0mOK")).TrimEnd() == "OK", "ESC recovery failed");
    }
}

static void UnderlineAndMetrics()
{
    var buffer = new TerminalBuffer(20, 5);
    var snapshot = buffer.Process("\x1b[4mA\x1b[4:0mB");
    Assert(!snapshot.Lines[0].Runs.Single(r => r.Text.Contains('B')).Style.Underline, "4:0 did not clear underline");
    buffer.SetCellMetrics(12, 25);
    snapshot = buffer.Process("\x1b[14t\x1b[16t");
    Assert(snapshot.Responses.Contains("\x1b[4;125;240t") && snapshot.Responses.Contains("\x1b[6;25;12t"), "pixel metrics are stale");
}

static void ArabicZwjDoesNotCollapseFollowingLetter()
{
    var snapshot = new TerminalBuffer(20, 5).Process("ب\u200dتX");
    Assert(snapshot.CursorColumn == 3,
        $"Arabic ZWJ collapsed the next letter; cursor column was {snapshot.CursorColumn}");
    Assert(LineText(snapshot.Lines[0]).StartsWith("ب\u200dتX", StringComparison.Ordinal),
        "Arabic ZWJ sequence lost logical text");
}

static void RegionalIndicatorsSurviveSgr()
{
    var snapshot = new TerminalBuffer(20, 5).Process("🇮\x1b[31m🇷X");
    Assert(snapshot.CursorColumn == 3,
        $"SGR split a flag cluster; cursor column was {snapshot.CursorColumn}");
    Assert(LineText(snapshot.Lines[0]).Contains('X'),
        "text after a styled flag was lost");
}

static void UnpairedSurrogateDoesNotThrow()
{
    TerminalSnapshot snapshot;
    try
    {
        snapshot = new TerminalBuffer(20, 5).Process("\uD800X");
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException(
            "unpaired high surrogate threw: " + exception.Message);
    }

    Assert(LineText(snapshot.Lines[0]).Contains('X'),
        "text after an unpaired surrogate was lost");
}

static void AlternateScreenClearsPendingWrap()
{
    var buffer = new TerminalBuffer(10, 6);
    buffer.Process("1234567890");
    var snapshot = buffer.Process("\x1b[?1049hX");
    Assert(snapshot.CursorRow == 0 && snapshot.CursorColumn == 1,
        $"alternate screen inherited pending wrap; cursor was {snapshot.CursorRow},{snapshot.CursorColumn}");
    Assert(LineText(snapshot.Lines[0]).TrimEnd().StartsWith('X'),
        "first alternate-screen character was displaced off row 0");
}

static void CanRecoversUnfinishedOsc()
{
    var snapshot = new TerminalBuffer(20, 5).Process("A\x1b]9;partial\x18OK");
    Assert(LineText(snapshot.Lines[0]).Contains("AOK"),
        "CAN left the parser consuming later output");
}

static void SgrColonSubparametersAreNotIndependentCodes()
{
    var snapshot = new TerminalBuffer(20, 5).Process("\x1b[31;4:3mX");
    var style = snapshot.Lines[0].Runs[0].Style;
    Assert(style.Foreground == new TerminalColor(197, 15, 31),
        "red foreground was lost");
    Assert(style.Underline, "underline subparameter was ignored");
    Assert(!style.Italic, "underline subparameter 3 was treated as italic");
}

static void Osc133MarksFollowReflow()
{
    var buffer = new TerminalBuffer(20, 8);
    buffer.Process("123456789012345\r\n");
    var marked = buffer.Process("\x1b]133;A\u0007prompt");
    Assert(marked.PromptRows.Count == 1 && marked.PromptRows[0] == 1,
        $"prompt started on row {string.Join(",", marked.PromptRows)} instead of 1");
    var resized = buffer.Resize(10, 8);
    Assert(resized.PromptRows.Count == 1 && LineText(resized.Lines[resized.PromptRows[0]]).Contains("prompt"),
        $"reflow left prompt mark on row {string.Join(",", resized.PromptRows)}");
}

static void SelectionRemapsAcrossReflow()
{
    var buffer = new TerminalBuffer(20, 8);
    var before = buffer.Process("123456789012345\r\nPROMPT");
    var after = buffer.Resize(10, 8);
    var mapped = TerminalBuffer.RemapSelection(before, (1, 0), (1, 6), after);
    Assert(mapped.Anchor is not null && mapped.End is not null,
        "reflow invalidated a still-mappable selection");
    var text = Slice(after, mapped.Anchor!.Value, mapped.End!.Value);
    Assert(text == "PROMPT", $"remapped selection copied '{text}' instead of PROMPT");
}

static void XtversionMatchesProductVersion()
{
    var snapshot = new TerminalBuffer(20, 5).Process("\x1b[>0q");
    Assert(snapshot.Responses.Any(response => response.Contains($"Satr({TerminalBuffer.ProductVersion})")),
        "XTVERSION does not match TerminalBuffer.ProductVersion");
}

static void ImeCompositionIsOverlayOnly()
{
    var ime = new ImeComposition();
    Assert(!ime.IsActive, "fresh composition should be idle");
    Assert(!ImeComposition.BlocksPtyKey(false, false), "idle IME must not swallow keys");
    ime.Set("مر", 1);
    Assert(ime.IsActive && ime.Text == "مر" && ime.Cursor == 1, "preedit was not stored");
    Assert(ImeComposition.BlocksPtyKey(true, false), "composition must hold Backspace/letters from the PTY");
    Assert(!ImeComposition.BlocksPtyKey(true, true), "Ctrl shortcuts must still reach the terminal");
    ime.Set(null);
    Assert(!ime.IsActive && ime.Text.Length == 0, "cancel did not clear preedit");
}

static void Utf8SplitsSurvive()
{
    var decoder = new Utf8OutputDecoder();
    var arabic = Encoding.UTF8.GetBytes("مرحبا");
    var assembled = new StringBuilder();
    foreach (var unit in arabic)
        assembled.Append(decoder.Decode([unit]));
    assembled.Append(decoder.Decode([], flush: true));
    Assert(assembled.ToString() == "مرحبا", "split Arabic UTF-8 did not reassemble");

    decoder = new Utf8OutputDecoder();
    var flag = Encoding.UTF8.GetBytes("🇮🇷X");
    assembled.Clear();
    foreach (var unit in flag)
        assembled.Append(decoder.Decode([unit]));
    assembled.Append(decoder.Decode([], flush: true));
    Assert(assembled.ToString() == "🇮🇷X", "split flag UTF-8 did not reassemble");

    decoder = new Utf8OutputDecoder();
    decoder.Decode([0xD9]);
    var flushed = decoder.Decode([], flush: true);
    Assert(flushed.Contains('\uFFFD'), "truncated UTF-8 at EOF was dropped instead of replaced");
}

static string Text(TerminalSnapshot snapshot) => string.Join(
    "\n",
    snapshot.Lines.Select(LineText));

static string LineText(TerminalLine line) =>
    string.Concat(line.Runs.Select(run => run.Text));

static string Slice(
    TerminalSnapshot snapshot,
    (int Row, int Offset) start,
    (int Row, int Offset) end)
{
    if (start.Row > end.Row || (start.Row == end.Row && start.Offset > end.Offset))
        (start, end) = (end, start);
    var result = new StringBuilder();
    for (var row = start.Row; row <= end.Row && row < snapshot.Lines.Count; row++)
    {
        var text = LineText(snapshot.Lines[row]);
        var from = row == start.Row ? Math.Min(start.Offset, text.Length) : 0;
        var to = row == end.Row ? Math.Min(end.Offset, text.Length) : text.Length;
        if (row > start.Row && !snapshot.Lines[row].WrappedFromPrevious)
            result.Append('\n');
        result.Append(text[from..Math.Max(from, to)]);
    }
    return result.ToString();
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
