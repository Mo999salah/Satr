using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Media.TextFormatting;
using Avalonia.Media;
namespace Satr;

/// <summary>A cell-based terminal surface. Only visible rows are shaped and drawn.</summary>
public sealed class TerminalView : ContentControl
{
    private readonly ScrollViewer _scroll;
    private readonly Surface _surface;
    private readonly ImeClient _imeClient;
    private readonly ImeComposition _ime = new();
    private TerminalSnapshot? _snapshot;
    private bool _smartRtl;
    private double _cellWidth = 8.5, _lineHeight = 18;
    private (int Row, int Offset)? _anchor, _end;
    private bool _dragging;
    private readonly Dictionary<int, RowLayout> _layouts = [];
    private readonly Dictionary<TerminalColor, Brush> _brushes = [];
    private string _fontKey = string.Empty;
    private string _searchQuery = "";
    private int _searchOffset = -1;
    private static readonly Regex Links = new(@"(?i)\b(?:https?://|www\.)[^\s<>{}\[\]""']+", RegexOptions.Compiled);
    public event Action<Uri>? LinkRequested;
    public event Action<bool>? FollowChanged;
    public event Action? ViewportChanged;
    public bool IsComposing => _ime.IsActive;
    public bool HasSelection => _anchor is not null && _end is not null && _anchor != _end;
    public double VerticalOffset => _scroll.Offset.Y;
    public double ViewportWidth => _scroll.Viewport.Width;
    public double ViewportHeight => _scroll.Viewport.Height;
    public bool IsUpdatingScroll { get; private set; }
    public ((int Row, int Offset)? Anchor, (int Row, int Offset)? End) SelectionState
    {
        get => (_anchor, _end);
        set { (_anchor, _end) = value; _surface.InvalidateVisual(); }
    }

    public TerminalView()
    {
        Focusable = true;
        Avalonia.Automation.AutomationProperties.SetName(this, "Satr terminal — drag to select, Shift+drag while captured, Ctrl+Click for links");
        ToolTip.SetTip(this, "Shift+drag selects while captured • Ctrl+Click opens links");
        _surface = new Surface(this) { VerticalAlignment = VerticalAlignment.Top };
        _imeClient = new ImeClient(this);
        TextInputOptions.SetMultiline(this, true);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = _imeClient);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _scroll = new ScrollViewer
        {
            Content = _surface,
            // Reserve a stable gutter: history appearing must not resize the PTY.
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,

            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Focusable = false
        };
        Content = _scroll;

        _scroll.ScrollChanged += (_, e) =>
        {
            // Scrolling and history growth do not change existing row geometry.
            if (e.ViewportDelta.X != 0) _layouts.Clear();
            if (e.ViewportDelta.X != 0 || e.ViewportDelta.Y != 0) ViewportChanged?.Invoke();
            _surface.InvalidateVisual();
            if (!IsUpdatingScroll && e.ExtentDelta.Y == 0 && e.OffsetDelta.Y != 0)
                FollowChanged?.Invoke(_scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 2);
        };
        SizeChanged += (_, _) => { _layouts.Clear(); _surface.InvalidateVisual(); };
        _surface.PointerPressed += BeginSelection;
        _surface.PointerMoved += ExtendSelection;
        _surface.PointerMoved += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && LinkAt(e.GetPosition(_surface)) is not null)
                Cursor = new Cursor(StandardCursorType.Hand);
            else Cursor = Cursor.Default;
        };
        _surface.PointerCaptureLost += (_, _) => _dragging = false;
        _surface.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        LostFocus += (_, _) => { if (_ime.IsActive) CancelComposition(); };
    }

    public void Present(TerminalSnapshot snapshot, bool smartRtl, double cellWidth,
        double lineHeight, bool followOutput)
    {
        IsUpdatingScroll = true;
        Dispatcher.UIThread.Post(() => IsUpdatingScroll = false, DispatcherPriority.Background);
        var trimmed = _snapshot is null ? 0 : snapshot.ScrollbackStartIndex - _snapshot.ScrollbackStartIndex;
        if (_snapshot?.Modes.AlternateScreen != snapshot.Modes.AlternateScreen)
            ClearSelection();
        else if (trimmed > 0)
        {
            if (_anchor is { } a && _end is { } b && a.Row >= trimmed && b.Row >= trimmed)
            { _anchor = (a.Row - (int)trimmed, a.Offset); _end = (b.Row - (int)trimmed, b.Offset); }
            else ClearSelection();
        }
        var fontKey = $"{FontFamily.Name}|{FontSize}|{FontWeight}|{FontStyle}|{(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)}";
        if (trimmed != 0 || _smartRtl != smartRtl || _cellWidth != cellWidth ||
            _lineHeight != lineHeight || _fontKey != fontKey ||
            _snapshot?.Modes.AlternateScreen != snapshot.Modes.AlternateScreen)
            _layouts.Clear();
        _fontKey = fontKey;
        _snapshot = snapshot;
        _smartRtl = smartRtl;
        _cellWidth = cellWidth;
        _lineHeight = lineHeight;
        _surface.Height = Math.Max(lineHeight, snapshot.Lines.Count * lineHeight);
        _surface.InvalidateVisual();
        if (followOutput) _scroll.ScrollToEnd();
        else if (trimmed > 0) ScrollToVerticalOffset(Math.Max(0, VerticalOffset - trimmed * lineHeight));
        _imeClient.NotifyCursor();
    }

    public void Clear()
    {
        _ime.Clear();
        _snapshot = null; ResetSearch();
        _layouts.Clear();
        _surface.Height = _lineHeight;
        ClearSelection();
        _surface.InvalidateVisual();
    }

    public void ScrollToVerticalOffset(double offset) => _scroll.Offset = new Vector(0, offset);
    public void ScrollToEnd() => _scroll.ScrollToEnd();
    public int FirstVisibleRow => _lineHeight <= 0 ? 0 : (int)(VerticalOffset / _lineHeight);
    public void ScrollToRow(int row)
    {
        ScrollToVerticalOffset(Math.Max(0, row * _lineHeight - _lineHeight));
        FollowChanged?.Invoke(false);
    }
    public bool TryGetGridCell(PointerEventArgs e, out int x, out int y)
    {
        var point = e.GetPosition(_surface);
        var row = (int)Math.Floor(point.Y / _lineHeight);
        var cell = _snapshot is not null && row >= 0 && row < _snapshot.Lines.Count
            ? Layout(row).Cells.FirstOrDefault(c => c.Width > 0 && point.X >= c.X && point.X < c.X + c.Width) : null;
        x = cell is null ? (int)Math.Floor(point.X / _cellWidth) + 1 : cell.Column + 1;
        y = (int)Math.Floor(point.Y / _lineHeight) - (_snapshot?.ScrollbackCount ?? 0) + 1;
        return point.X >= 0 && point.X < ViewportWidth && point.Y >= VerticalOffset &&
            point.Y < VerticalOffset + ViewportHeight && y > 0;
    }
    public void ClearSelection() { _anchor = _end = null; _surface.InvalidateVisual(); }
    public void SelectAll()
    {
        if (_snapshot is not { Lines.Count: > 0 } snapshot) return;
        _anchor = (0, 0);
        _end = (snapshot.Lines.Count - 1, Text(snapshot.Lines[^1]).Length);
        _surface.InvalidateVisual();
    }

    public string GetSelectedText()
    {
        if (!HasSelection || _snapshot is null) return string.Empty;
        var (start, end) = SelectionRange();
        var result = new StringBuilder();
        for (var row = start.Row; row <= end.Row && row < _snapshot.Lines.Count; row++)
        {
            var text = Text(_snapshot.Lines[row]);
            var from = row == start.Row ? Math.Min(start.Offset, text.Length) : 0;
            var to = row == end.Row ? Math.Min(end.Offset, text.Length) : text.Length;
            if (row > start.Row && !_snapshot.Lines[row].WrappedFromPrevious) result.AppendLine();
            result.Append(text[from..Math.Max(from, to)]);
        }
        return result.ToString();
    }

    public void ResetSearch() { _searchQuery = ""; _searchOffset = -1; }
    public (int Index, int Count) Find(string query, int direction, bool matchCase = false)
    {
        if (_snapshot is null || query.Length == 0) { ResetSearch(); ClearSelection(); return (0, 0); }
        var key = (matchCase ? "1" : "0") + query;
        if (_searchQuery != key) { _searchQuery = key; _searchOffset = -1; }
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = new StringBuilder(); var starts = new int[_snapshot.Lines.Count];
        for (var row = 0; row < starts.Length; row++)
        {
            if (row > 0 && !_snapshot.Lines[row].WrappedFromPrevious) text.Append('\n');
            starts[row] = text.Length;
            foreach (var run in _snapshot.Lines[row].Runs) text.Append(run.Style.Hidden ? new string(' ', run.Text.Length) : run.Text);
        }
        var content = text.ToString();
        var count = 0; var chosen = -1; var chosenIndex = 0; var first = -1; var last = -1;
        for (var offset = content.IndexOf(query, comparison); offset >= 0;
             offset = content.IndexOf(query, offset + Math.Max(1, query.Length), comparison))
        {
            count++; if (first < 0) first = offset; last = offset;
            if (direction > 0 ? chosen < 0 && offset > _searchOffset : _searchOffset < 0 || offset < _searchOffset)
            { chosen = offset; chosenIndex = count; }
        }
        if (count == 0) { ClearSelection(); return (0, 0); }
        if (chosen < 0) { chosen = direction > 0 ? first : last; chosenIndex = direction > 0 ? 1 : count; }
        (int Row, int Offset) PositionAt(int offset)
        {
            var row = 0;
            while (row + 1 < starts.Length && starts[row + 1] <= offset) row++;
            return (row, Math.Min(offset - starts[row], Text(_snapshot.Lines[row]).Length));
        }
        _searchOffset = chosen; _anchor = PositionAt(chosen); _end = PositionAt(chosen + query.Length);
        ScrollToVerticalOffset(Math.Max(0, _anchor.Value.Row * _lineHeight - _lineHeight));
        FollowChanged?.Invoke(false); _surface.InvalidateVisual();
        return (chosenIndex, count);
    }

    private ((int Row, int Offset) Start, (int Row, int Offset) End) SelectionRange()
    {
        var a = _anchor!.Value; var b = _end!.Value;
        return a.Row < b.Row || a.Row == b.Row && a.Offset <= b.Offset ? (a, b) : (b, a);
    }

    public bool IsLinkAt(PointerEventArgs e) => LinkAt(e.GetPosition(_surface)) is not null;

    private Uri? LinkAt(Point point)
    {
        if (_snapshot is null || point.Y < VerticalOffset || point.Y >= VerticalOffset + ViewportHeight ||
            point.X < 0 || point.X >= ViewportWidth) return null;
        var row = (int)(point.Y / _lineHeight);
        if (row < 0 || row >= _snapshot.Lines.Count) return null;
        var layout = Layout(row);
        var cell = layout.Cells.FirstOrDefault(cell => point.X >= cell.X && point.X < cell.X + cell.Width);
        if (cell is null || cell.Style.Hidden) return null;
        if (!string.IsNullOrEmpty(cell.Style.Hyperlink) &&
            Uri.TryCreate(cell.Style.Hyperlink, UriKind.Absolute, out var tagged))
            return tagged;
        foreach (Match match in Links.Matches(layout.Text))
        {
            var address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
            if (cell.Start < match.Index || cell.Start >= match.Index + address.Length) continue;
            if (address.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) address = "https://" + address;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri)) return uri;
        }
        return null;
    }

    private void BeginSelection(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
        Focus();
        var hit = Hit(e.GetPosition(_surface));
        if (hit is null) return;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && LinkAt(e.GetPosition(_surface)) is { } uri)
        {
            LinkRequested?.Invoke(uri);
            e.Handled = true;
            return;
        }
        _anchor = _end = hit;
        if (e.ClickCount == 2 && _snapshot is not null)
        {
            var text = Text(_snapshot.Lines[hit.Value.Row]);
            var start = Math.Min(hit.Value.Offset, text.Length); var end = start;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            _anchor = (hit.Value.Row, start); _end = (hit.Value.Row, end);
        }
        _dragging = true;
        e.Pointer.Capture(_surface);
        _surface.InvalidateVisual();
        e.Handled = true;
    }

    private void ExtendSelection(object? sender, PointerEventArgs e)
    {
        if (!_dragging || !e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(_surface);
        if (point.Y < VerticalOffset) ScrollToVerticalOffset(VerticalOffset - _lineHeight);
        if (point.Y > VerticalOffset + _scroll.Viewport.Height) ScrollToVerticalOffset(VerticalOffset + _lineHeight);
        _end = Hit(point);
        _surface.InvalidateVisual();
        e.Handled = true;
    }

    private (int Row, int Offset)? Hit(Point point)
    {
        if (_snapshot is not { Lines.Count: > 0 }) return null;
        var row = Math.Clamp((int)(point.Y / _lineHeight), 0, _snapshot.Lines.Count - 1);
        var layout = Layout(row);
        var closest = layout.Cells.FirstOrDefault(cell => point.X >= cell.X && point.X < cell.X + cell.Width)
            ?? layout.Cells.MinBy(cell => Math.Abs(cell.X + cell.Width / 2 - point.X));
        if (closest is null) return (row, 0);
        var after = point.X >= closest.X + closest.Width / 2;
        return (row, (after != closest.Rtl) ? closest.Start + closest.Length : closest.Start);
    }

    private RowLayout Layout(int row)
    {
        var line = _snapshot!.Lines[row];
        // Screen snapshots recreate line objects even when their contents are unchanged.
        if (_layouts.TryGetValue(row, out var cached) &&
            (ReferenceEquals(cached.Source, line) ||
             cached.Source.CellLength == line.CellLength && cached.Source.Runs.SequenceEqual(line.Runs)))
            return cached;
        var text = Text(line);
        var rightAlign = SmartRtl.ShouldRightAlign(line, _smartRtl, _snapshot.Modes.AlternateScreen);
        var spans = _smartRtl && line.ContainsRightToLeft
            ? SmartRtl.GetDirectionalSpans(text, rightAlign)
            : text.Length == 0 ? [] : new[] { new DirectionalSpan(0, text.Length, false) };
        var cells = new List<DrawCell>();
        var glyphs = new List<DrawGlyph>();
        var widths = new List<(int Start, int Length, int Width, int Column)>();
        var logicalColumn = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var width = TerminalBuffer.GetTextElementWidth(element);
            widths.Add((enumerator.ElementIndex, element.Length, width, logicalColumn));
            logicalColumn += width;
        }
        var totalWidth = widths.Sum(item => item.Width) * _cellWidth;
        var x = rightAlign ? Math.Max(0, _scroll.Viewport.Width - totalWidth) : 0;
        var orderedSpans = spans;
        foreach (var span in orderedSpans)
        {
            var members = widths.Where(item => item.Start >= span.Start && item.Start < span.Start + span.Length).ToArray();
            var spanWidth = members.Sum(item => item.Width) * _cellWidth;
            var advance = 0.0;
            foreach (var member in members)
            {
                var width = member.Width * _cellWidth;
                var cellX = x + (span.IsRightToLeft ? spanWidth - advance - width : advance);
                var style = StyleAt(line, member.Start);
                cells.Add(new DrawCell(member.Start, member.Length, cellX, width, span.IsRightToLeft, style, member.Column, member.Width));
                advance += width;
                // Latin and box-drawing glyphs are positioned at exact cell boundaries.
                if (!span.IsRightToLeft && width > 0)
                    glyphs.Add(new DrawGlyph(text.Substring(member.Start, member.Length), cellX, width, false, member.Start, style));
            }
            // Shape a complete RTL span together so Arabic joining survives ANSI style boundaries.
            if (span.IsRightToLeft && members.Length > 0)
                glyphs.Add(new DrawGlyph(text.Substring(span.Start, span.Length), x, spanWidth, true, span.Start, StyleAt(line, span.Start)));
            x += spanWidth;
        }
        foreach (var glyph in glyphs.Where(g => g.Rtl && g.Width > 0))
        {
            var formatted = FormatGlyph(line, glyph, Links.Matches(text));
            if (formatted.WidthIncludingTrailingWhitespace <= 0) continue;
            var scale = glyph.Width / formatted.WidthIncludingTrailingWhitespace;
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.Start < glyph.Start || cell.Start + cell.Length > glyph.Start + glyph.Text.Length || cell.Columns == 0) continue;
                // FormattedText ranges are 0-based UTF-16. The old +1 overflowed count and crashed the render loop.
                var start = cell.Start - glyph.Start;
                if ((uint)start >= (uint)glyph.Text.Length || start + cell.Length > glyph.Text.Length) continue;
                var bounds = formatted.BuildHighlightGeometry(new Point(0, 0), start, cell.Length)?.Bounds;
                if (bounds is { Width: > 0 } box)
                    cells[i] = cell with { X = glyph.X + box.X * scale, Width = box.Width * scale };
            }
        }
        var result = new RowLayout(line, text, cells, glyphs);
        _layouts[row] = result;
        return result;
    }

    private static TerminalStyle StyleAt(TerminalLine line, int offset)
    {
        foreach (var run in line.Runs) { if (offset < run.Text.Length) return run.Style; offset -= run.Text.Length; }
        return default;
    }
    private static string Text(TerminalLine line) => string.Concat(line.Runs.Select(run => run.Text));
    private Brush BrushFor(TerminalColor color)
    {
        if (_brushes.TryGetValue(color, out var cached)) return cached;
        if (_brushes.Count >= 1024) _brushes.Clear();
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));

        _brushes[color] = brush;
        return brush;
    }
    private static (TerminalColor Foreground, TerminalColor Background) Colors(TerminalStyle style)
    {
        var fg = style.Foreground ?? new TerminalColor(230, 230, 230);
        var bg = style.Background ?? new TerminalColor(25, 25, 25);
        if (style.Inverse) (fg, bg) = (bg, fg);
        if (style.Dim) fg = new((byte)(fg.Red * .55 + bg.Red * .45), (byte)(fg.Green * .55 + bg.Green * .45), (byte)(fg.Blue * .55 + bg.Blue * .45));
        if (style.Hidden) fg = bg;
        return (fg, bg);
    }

    private void Draw(DrawingContext dc)
    {
        if (_snapshot is null) return;
        var top = VerticalOffset;
        using var viewportClip = dc.PushClip(new Rect(0, top, Math.Max(0, _scroll.Viewport.Width), Math.Max(0, _scroll.Viewport.Height)));
        dc.DrawRectangle(Background, null, new Rect(0, top, Math.Max(0, _scroll.Viewport.Width), Math.Max(0, _scroll.Viewport.Height)));
        if (_snapshot.Lines.All(line => string.IsNullOrWhiteSpace(Text(line))))
        {
            // Fully blank = new session without output; show a hint instead of a black void.
            var hint = new FormattedText("Waiting for output — Ctrl+Shift+T new session • Ctrl+Shift+F search",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily), 13, Brushes.Gray);
            dc.DrawText(hint, new Point(12, top + 10));
        }
        var first = Math.Max(0, (int)(top / _lineHeight));
        var last = Math.Min(_snapshot.Lines.Count, (int)Math.Ceiling((top + _scroll.Viewport.Height) / _lineHeight) + 1);
        foreach (var key in _layouts.Keys.Where(key => key < first || key >= last).ToArray()) _layouts.Remove(key);
        for (var row = first; row < last; row++)
        {
            var layout = Layout(row); var y = row * _lineHeight;
            using (dc.PushTransform(Matrix.CreateTranslation(0, y))) DrawRow(dc, layout);
            if (HasSelection)
            {
                var (start, end) = SelectionRange();
                if (row >= start.Row && row <= end.Row)
                    foreach (var cell in layout.Cells.Where(cell => (row != start.Row || cell.Start + cell.Length > start.Offset) && (row != end.Row || cell.Start < end.Offset)))
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100, 70, 125, 200)), null, new Rect(cell.X, y, cell.Width, _lineHeight));
            }
            if (_snapshot.CursorVisible && row == _snapshot.CursorRow)
            {
                var cursor = layout.Cells.FirstOrDefault(cell => cell.Columns > 0 && _snapshot.CursorColumn >= cell.Column && _snapshot.CursorColumn < cell.Column + cell.Columns);
                var rect = new Rect(cursor?.X ?? _snapshot.CursorColumn * _cellWidth, y, Math.Max(1, cursor?.Width ?? _cellWidth), _lineHeight);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 230, 230, 230)), new Pen(Brushes.LightGray, 1), rect);
            }
        }
        if (_ime.IsActive)
            DrawPreedit(dc);
    }

    private void DrawPreedit(DrawingContext dc)
    {
        var bounds = CursorBounds();
        using var formatted = PreeditLayout();
        var width = Math.Max(bounds.Width, formatted.WidthIncludingTrailingWhitespace);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 70, 125, 200)), null,
            new Rect(bounds.X, bounds.Y, width, bounds.Height));
        formatted.Draw(dc, new Point(bounds.X, bounds.Y + Math.Max(0, (_lineHeight - formatted.Height) / 2)));
        var caret = PreeditCursorBounds();
        dc.DrawLine(new Pen(Brushes.White, 1), caret.TopLeft, caret.BottomLeft);
        dc.DrawLine(new Pen(Brushes.LightGray, 1),
            new Point(bounds.X, bounds.Y + bounds.Height - 1),
            new Point(bounds.X + width, bounds.Y + bounds.Height - 1));
    }

    private TextLayout PreeditLayout() => new(_ime.Text, new Typeface(FontFamily), FontSize, Brushes.White);

    private Rect PreeditCursorBounds()
    {
        var origin = CursorBounds();
        if (!_ime.IsActive) return origin;
        using var layout = PreeditLayout();
        var caret = layout.HitTestTextPosition(_ime.Cursor ?? _ime.Text.Length);
        return new Rect(origin.X + caret.X, origin.Y, 1, _lineHeight);
    }

    public Rect CursorBounds()
    {
        if (_snapshot is null)
            return new Rect(0, 0, _cellWidth, _lineHeight);
        var y = _snapshot.CursorRow * _lineHeight;
        if (_snapshot.CursorRow < 0 || _snapshot.CursorRow >= _snapshot.Lines.Count)
            return new Rect(_snapshot.CursorColumn * _cellWidth, y, _cellWidth, _lineHeight);
        var cell = Layout(_snapshot.CursorRow).Cells.FirstOrDefault(item =>
            item.Columns > 0 && _snapshot.CursorColumn >= item.Column &&
            _snapshot.CursorColumn < item.Column + item.Columns);
        return new Rect(cell?.X ?? _snapshot.CursorColumn * _cellWidth, y,
            Math.Max(1, cell?.Width ?? _cellWidth), _lineHeight);
    }

    public void CancelComposition()
    {
        _ime.Clear();
        _imeClient.Reset();
        _surface.InvalidateVisual();
    }
    private void DrawRow(DrawingContext dc, RowLayout layout)
    {
        const double y = 0;
        // Merge adjacent backgrounds: fewer draw calls and no fractional-cell seams.
        var ordered = layout.Cells.OrderBy(cell => cell.X).ToArray();
        for (var index = 0; index < ordered.Length;)
        {
            var cell = ordered[index++];
            var color = Colors(cell.Style).Background;
            var right = cell.X + cell.Width;
            while (index < ordered.Length && Math.Abs(ordered[index].X - right) < .01 &&
                Colors(ordered[index].Style).Background == color)
            {
                right = ordered[index].X + ordered[index].Width;
                index++;
            }
            dc.DrawRectangle(BrushFor(color), null, new Rect(cell.X, y, right - cell.X, _lineHeight));
        }
        var links = Links.Matches(layout.Text);
        foreach (var glyph in layout.Glyphs)
        {
            if (DrawBlock(dc, glyph, y) || DrawBox(dc, glyph, y)) continue;
            var formatted = FormatGlyph(layout.Source, glyph, links);
            using var glyphClip = dc.PushClip(new Rect(glyph.X, y, glyph.Width, _lineHeight));
            var scale = glyph.Rtl && formatted.WidthIncludingTrailingWhitespace > 0 ? glyph.Width / formatted.WidthIncludingTrailingWhitespace : 1;
            var origin = glyph.X;
            using var glyphScale = dc.PushTransform(Matrix.CreateTranslation(-origin, -y) * Matrix.CreateScale(scale, 1) * Matrix.CreateTranslation(origin, y));
            dc.DrawText(formatted, new Point(origin, y + Math.Max(0, (_lineHeight - formatted.Height) / 2)));
    
        }
        foreach (var hidden in layout.Cells.Where(cell => cell.Style.Hidden))
            dc.DrawRectangle(BrushFor(Colors(hidden.Style).Background), null,
                new Rect(hidden.X, y, hidden.Width, _lineHeight));
    }

    private FormattedText FormatGlyph(TerminalLine source, DrawGlyph glyph, MatchCollection links)
    {
        if (glyph.Formatted is { } cached) return cached;
        FormattedText formatted;
            // Keep terminal rows LTR. Avalonia shapes Arabic as an embedded RTL run
            // without changing the row origin or adding bidi override characters.
            formatted = new FormattedText(glyph.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(glyph.Text.EnumerateRunes().Any(rune => rune.Value >= 0x1f000)
                    ? new FontFamily(OperatingSystem.IsWindows() ? "Segoe UI Emoji" : "Noto Color Emoji") : FontFamily, glyph.Style.Italic ? FontStyle.Italic : FontStyle,
                    glyph.Style.Bold ? FontWeight.Bold : FontWeight, FontStretch), FontSize,
                BrushFor(Colors(glyph.Style).Foreground));
            for (var index = 0; index < glyph.Text.Length;)
            {
                var style = StyleAt(source, glyph.Start + index);
                var end = index + 1;
                while (end < glyph.Text.Length && StyleAt(source, glyph.Start + end) == style) end++;
                formatted.SetForegroundBrush(BrushFor(Colors(style).Foreground), index, end - index);
                formatted.SetFontWeight(style.Bold ? FontWeight.Bold : FontWeight, index, end - index);
                formatted.SetFontStyle(style.Italic ? FontStyle.Italic : FontStyle, index, end - index);
                var decorations = new TextDecorationCollection();
                if (style.Underline || !string.IsNullOrEmpty(style.Hyperlink))
                    decorations.Add(TextDecorations.Underline[0]);
                if (style.Strikethrough) decorations.Add(TextDecorations.Strikethrough[0]);
                formatted.SetTextDecorations(decorations, index, end - index);
                if (!string.IsNullOrEmpty(style.Hyperlink))
                    formatted.SetForegroundBrush(Brushes.CornflowerBlue, index, end - index);
                index = end;
            }
            foreach (Match link in links)
            {
                var start = Math.Max(glyph.Start, link.Index); var end = Math.Min(glyph.Start + glyph.Text.Length, link.Index + link.Length);
                if (end <= start) continue;
                if (glyph.Style.Hidden) continue;
                formatted.SetForegroundBrush(Brushes.CornflowerBlue, start - glyph.Start, end - start);
                formatted.SetTextDecorations(TextDecorations.Underline, start - glyph.Start, end - start);
            }
        return glyph.Formatted = formatted;
    }

    private sealed class Surface(TerminalView owner) : Control
    {
        public override void Render(DrawingContext drawingContext) => owner.Draw(drawingContext);
    }

    private sealed class ImeClient(TerminalView view) : TextInputMethodClient
    {
        public override Visual TextViewVisual => view._surface;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => "";
        public override Rect CursorRectangle => view.PreeditCursorBounds();
        public override TextSelection Selection { get => default; set { } }

        public override void SetPreeditText(string? preeditText) =>
            SetPreeditText(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos)
        {
            view._ime.Set(preeditText, cursorPos);
            view._surface.InvalidateVisual();
            RaiseCursorRectangleChanged();
        }

        public void NotifyCursor() => RaiseCursorRectangleChanged();
        public void Reset() => RequestReset();
    }
    private sealed record DrawCell(int Start, int Length, double X, double Width, bool Rtl, TerminalStyle Style, int Column, int Columns);
    private sealed record DrawGlyph(string Text, double X, double Width, bool Rtl, int Start, TerminalStyle Style)
    {
        public FormattedText? Formatted { get; set; }
    }
    private sealed record RowLayout(TerminalLine Source, string Text, List<DrawCell> Cells, List<DrawGlyph> Glyphs)
    {

    }

    private bool DrawBlock(DrawingContext dc, DrawGlyph glyph, double y)
    {
        if (glyph.Text.Length != 1 || glyph.Text[0] is < '\u2580' or > '\u259f') return false;
        var code = glyph.Text[0];
        var (foreground, background) = Colors(glyph.Style);
        var brush = BrushFor(foreground);
        void Fill(double left, double top, double width, double height) =>
            dc.DrawRectangle(brush, null, new Rect(glyph.X + left * glyph.Width,
                y + top * _lineHeight, width * glyph.Width, height * _lineHeight));

        if (code == '\u2580') Fill(0, 0, 1, .5);
        else if (code <= '\u2588')
        {
            var height = (code - 0x2580) / 8.0;
            Fill(0, 1 - height, 1, height);
        }
        else if (code <= '\u258f') Fill(0, 0, (0x2590 - code) / 8.0, 1);
        else if (code == '\u2590') Fill(.5, 0, .5, 1);
        else if (code <= '\u2593')
        {
            var amount = (code - 0x2590) / 4.0;
            byte Blend(byte fg, byte bg) => (byte)Math.Round(fg * amount + bg * (1 - amount));
            brush = BrushFor(new TerminalColor(Blend(foreground.Red, background.Red),
                Blend(foreground.Green, background.Green), Blend(foreground.Blue, background.Blue)));
            Fill(0, 0, 1, 1);
        }
        else if (code == '\u2594') Fill(0, 0, 1, .125);
        else if (code == '\u2595') Fill(.875, 0, .125, 1);
        else
        {
            // Quadrants: upper-left, upper-right, lower-left, lower-right.
            var mask = code switch
            {
                '\u2596' => 4, '\u2597' => 8, '\u2598' => 1, '\u2599' => 13,
                '\u259a' => 9, '\u259b' => 7, '\u259c' => 11, '\u259d' => 2,
                '\u259e' => 6, '\u259f' => 14, _ => 0
            };
            for (var quadrant = 0; quadrant < 4; quadrant++)
                if ((mask & (1 << quadrant)) != 0)
                    Fill((quadrant % 2) * .5, (quadrant / 2) * .5, .5, .5);
        }
        return true;
    }

    private bool DrawBox(DrawingContext dc, DrawGlyph glyph, double y)
    {
        // Edges meet at cell boundaries regardless of font metrics or DPI.
        var edges = glyph.Text switch
        {
            "─" or "━" or "═" => 3,
            "│" or "┃" or "║" => 12,
            "┌" or "┏" or "╔" or "╭" => 10,
            "┐" or "┓" or "╗" or "╮" => 9,
            "└" or "┗" or "╚" or "╰" => 6,
            "┘" or "┛" or "╝" or "╯" => 5,
            "├" or "┣" or "╠" => 14,
            "┤" or "┫" or "╣" => 13,
            "┬" or "┳" or "╦" => 11,
            "┴" or "┻" or "╩" => 7,
            "┼" or "╋" or "╬" => 15,
            _ => 0
        };
        if (edges == 0) return false;
        var foreground = BrushFor(Colors(glyph.Style).Foreground);
        var pen = new Pen(foreground, glyph.Text is "━" or "┃" or "┏" or "┓" or "┗" or "┛" or "┣" or "┫" or "┳" or "┻" or "╋" ? 2 : 1);
        var middle = new Point(glyph.X + glyph.Width / 2, y + _lineHeight / 2);
        double[] offsets = "═║╔╗╚╝╠╣╦╩╬".Contains(glyph.Text, StringComparison.Ordinal) ? [-1.5, 1.5] : [0];
        foreach (var offset in offsets)
        {
            if ((edges & 1) != 0) dc.DrawLine(pen, new Point(glyph.X, middle.Y + offset), new Point(middle.X + offset, middle.Y + offset));
            if ((edges & 2) != 0) dc.DrawLine(pen, new Point(middle.X + offset, middle.Y + offset), new Point(glyph.X + glyph.Width, middle.Y + offset));
            if ((edges & 4) != 0) dc.DrawLine(pen, new Point(middle.X + offset, y), new Point(middle.X + offset, middle.Y + offset));
            if ((edges & 8) != 0) dc.DrawLine(pen, new Point(middle.X + offset, middle.Y + offset), new Point(middle.X + offset, y + _lineHeight));
        }
        return true;
    }
}
