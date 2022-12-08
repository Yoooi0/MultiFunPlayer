using System.Collections;

namespace MultiFunPlayer.Common;

public class ChapterCollection : IReadOnlyList<Chapter>
{
    private readonly List<Chapter> _items;

    public ChapterCollection() => _items = new List<Chapter>();
    public ChapterCollection(int capacity) => _items = new List<Chapter>(capacity);

    public bool Add(string name, TimeSpan startPosition, TimeSpan endPosition)
    {
        if (startPosition > endPosition)
            (startPosition, endPosition) = (endPosition, startPosition);

        foreach (var chapter in _items)
            if (chapter.StartPosition >= startPosition && chapter.EndPosition <= endPosition)
                return false;

        var startIntersect = Find(startPosition);
        var endIntersect = Find(endPosition);
        if (startIntersect == endIntersect && startIntersect != null)
            return false;

        if (startIntersect != null)
            startPosition = startIntersect.EndPosition;

        if (endIntersect != null)
            endPosition = endIntersect.StartPosition;

        _items.Add(new Chapter(name, startPosition, endPosition));
        return true;
    }

    public Chapter Find(TimeSpan position)
    {
        if (_items.Count == 0)
            return null;

        foreach(var chapter in _items)
            if (position >= chapter.StartPosition && position <= chapter.EndPosition)
                return chapter;

        return null;
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
