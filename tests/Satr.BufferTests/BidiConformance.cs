using System.Globalization;
using System.Text;
using Satr;

internal static class BidiConformance
{
    public static int Run(string path)
    {
        var passed = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var fields = line.Split(';');
            var runes = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(hex => new Rune(int.Parse(hex, NumberStyles.HexNumber))).ToArray();
            var text = string.Concat(runes.Select(r => r.ToString()));
            var rightToLeft = fields[1] == "2" ? SmartRtl.BaseRightToLeft(text) : fields[1] == "1";
            var levels = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var offsets = new List<(int Offset, int Index)>();
            var offset = 0;
            for (var i = 0; i < runes.Length; i++) { offsets.Add((offset, i)); offset += runes[i].Utf16SequenceLength; }
            var actual = new List<int>();
            foreach (var span in SmartRtl.GetDirectionalSpans(text, rightToLeft))
            {
                var items = offsets.Where(i => i.Offset >= span.Start && i.Offset < span.Start + span.Length);
                if (span.IsRightToLeft) items = items.Reverse();
                actual.AddRange(items.Where(i => levels[i.Index] != "x").Select(i => i.Index));
            }
            var expected = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
            if (!actual.SequenceEqual(expected)) throw new Exception($"BiDi visual order failed after {passed} cases (RTL={rightToLeft}, actual={string.Join(",", actual)}): {line}");
            passed++;
        }
        Console.WriteLine($"PASS {passed} Unicode BidiCharacterTest visual-order cases (through L2; shaping tested separately)");
        return 0;
    }
}
