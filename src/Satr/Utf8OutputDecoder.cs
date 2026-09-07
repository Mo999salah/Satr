using System.Text;

namespace Satr;

/// <summary>Stateful UTF-8 decode so a code point split across PTY reads is not dropped.</summary>
internal sealed class Utf8OutputDecoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private char[] _chars = new char[32769];

    public string Decode(ReadOnlySpan<byte> bytes, bool flush = false)
    {
        if (bytes.IsEmpty && !flush)
            return "";
        var max = Encoding.UTF8.GetMaxCharCount(bytes.Length) + 1;
        if (max > _chars.Length)
            _chars = new char[max];
        var length = _decoder.GetChars(bytes, _chars.AsSpan(), flush);
        return length <= 0 ? "" : new string(_chars, 0, length);
    }
}
