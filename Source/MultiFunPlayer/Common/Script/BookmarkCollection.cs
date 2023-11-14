using System.Collections;

namespace MultiFunPlayer.Common;

public class BookmarkCollection : IReadOnlyList<Bookmark>
{
    private readonly List<Bookmark> _items;

    public BookmarkCollection() => _items = [];
    public BookmarkCollection(int capacity) => _items = new List<Bookmark>(capacity);

    public void Add(string name, TimeSpan position) => Add(name, position.TotalSeconds);
    public void Add(string name, double position)
    {
        var index = SearchForIndexAfter(position);
        _items.Insert(index, new Bookmark(name, position));
    }

    public bool TryFindByName(string name, out Bookmark bookmark)
    {
        bookmark = _items.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return bookmark != null;
    }

    public int SearchForIndexBefore(double position) => SearchForIndexAfter(position) - 1;
    public int SearchForIndexAfter(double position)
    {
        if (_items.Count == 0 || position < _items[0].Position)
            return 0;

        if (position > _items[^1].Position)
            return Count;

        var bestIndex = _items.BinarySearch(new Bookmark(null, position), BookmarkPositionComparer.Default);
        if (bestIndex >= 0)
            return bestIndex;

        bestIndex = ~bestIndex;
        return bestIndex == Count ? Count : bestIndex;
    }

    #region IReadOnlyList
    public Bookmark this[int index] => _items[index];
    #endregion

    #region IReadOnlyCollection
    public int Count => _items.Count;
    #endregion

    #region IEnumerable
    public IEnumerator<Bookmark> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    #endregion
}
