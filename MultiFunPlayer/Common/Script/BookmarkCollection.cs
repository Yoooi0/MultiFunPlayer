using System.Collections;

namespace MultiFunPlayer.Common;

public class BookmarkCollection : IReadOnlyList<Bookmark>
{
    private readonly List<Bookmark> _items;

    public BookmarkCollection() => _items = new List<Bookmark>();
    public BookmarkCollection(int capacity) => _items = new List<Bookmark>(capacity);

    public void Add(string name, TimeSpan position) => Add(name, position.TotalSeconds);
    public void Add(string name, double position) => _items.Add(new Bookmark(name, position));

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
