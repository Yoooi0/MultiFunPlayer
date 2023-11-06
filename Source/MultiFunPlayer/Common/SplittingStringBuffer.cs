using System.Numerics;

namespace MultiFunPlayer.Common;

internal class SplittingStringBuffer
{
    private readonly char _separator;
    private char[] _buffer;
    private int _index;

    public SplittingStringBuffer(char separator)
    {
        _separator = separator;
        _buffer = Array.Empty<char>();
        _index = 0;
    }

    public void Push(ReadOnlySpan<char> s)
    {
        var requiredLength = _index + s.Length;
        if (requiredLength > _buffer.Length)
            Array.Resize(ref _buffer, (int)BitOperations.RoundUpToPowerOf2((uint)requiredLength));

        s.CopyTo(_buffer.AsSpan(_index));
        _index += s.Length;
    }

    public IEnumerable<string> Consume()
    {
        if (_index == 0)
            yield break;

        var startIndex = 0;
        var endIndex = -1;
        while ((endIndex = Array.IndexOf(_buffer, _separator, startIndex)) >= 0)
        {
            yield return new string(_buffer.AsSpan(new Range(startIndex, endIndex)));
            startIndex = endIndex + 1;
        }

        if (startIndex == 0)
            yield break;

        _buffer.AsSpan(new Range(startIndex, _index)).CopyTo(_buffer);
        _index -= startIndex;

        Array.Fill(_buffer, '\0', _index, startIndex);
    }
}