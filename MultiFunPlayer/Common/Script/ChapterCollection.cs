using System.Collections;

namespace MultiFunPlayer.Common;

public class ChapterCollection : IReadOnlyList<Chapter>
{
    private readonly List<Chapter> _items;

    public ChapterCollection() => _items = new List<Chapter>();
    public ChapterCollection(int capacity) => _items = new List<Chapter>(capacity);

    public bool Add(string name, TimeSpan startPosition, TimeSpan endPosition) => Add(name, startPosition.TotalSeconds, endPosition.TotalSeconds);
    public bool Add(string name, double startPosition, double endPosition)
    {
        if (startPosition > endPosition)
            (startPosition, endPosition) = (endPosition, startPosition);

        foreach (var chapter in _items)
            if (chapter.StartPosition >= startPosition && chapter.EndPosition <= endPosition)
                return false;

        _ = TryFindIntersecting(startPosition, out var startIntersect);
        _ = TryFindIntersecting(endPosition, out var endIntersect);
        if (startIntersect == endIntersect && startIntersect != null)
            return false;

        if (startIntersect != null)
            startPosition = startIntersect.EndPosition;

        if (endIntersect != null)
            endPosition = endIntersect.StartPosition;

        _items.Add(new Chapter(name, startPosition, endPosition));
        return true;
    }

    public bool TryFindIntersecting(double position, out Chapter chapter)
    {
        chapter = _items.Find(x => position >= x.StartPosition && position <= x.EndPosition);
        return chapter != null;
    }

    public bool TryFindByName(string name, out Chapter chapter)
    {
        chapter = _items.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return chapter != null;
    }

    public int SearchForIndexBefore(double position) => SearchForIndexAfter(position) - 1;
    public int SearchForIndexAfter(double position)
    {
        if (_items.Count == 0 || position < _items[0].StartPosition)
            return 0;

        if (position > _items[^1].StartPosition)
            return Count;

        var bestIndex = _items.BinarySearch(new Chapter(null, position, position), ChapterStartPositionComparer.Default);
        if (bestIndex >= 0)
            return bestIndex;

        bestIndex = ~bestIndex;
        return bestIndex == Count ? Count : bestIndex;
    }

    #region IReadOnlyList
    public Chapter this[int index] => _items[index];
    #endregion

    #region IReadOnlyCollection
    public int Count => _items.Count;
    #endregion

    #region IEnumerable
    public IEnumerator<Chapter> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    #endregion
}
