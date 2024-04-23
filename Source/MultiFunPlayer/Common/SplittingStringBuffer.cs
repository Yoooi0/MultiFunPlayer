using System.Numerics;

namespace MultiFunPlayer.Common;

internal sealed class SplittingStringBuffer(char separator)
{
    private char[] _buffer = [];
    private int _index;

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

        int endIndex;
        var currentIndex = 0;
        while ((endIndex = Array.IndexOf(_buffer, separator, currentIndex)) >= 0)
        {
            yield return new string(_buffer.AsSpan(new Range(currentIndex, endIndex)));
            currentIndex = endIndex + 1;
        }

        if (currentIndex == 0)
            yield break;

        _buffer.AsSpan(new Range(currentIndex, _index)).CopyTo(_buffer);
        _buffer.AsSpan(new Range(_index - currentIndex, _index)).Clear();
        _index -= currentIndex;
    }
}