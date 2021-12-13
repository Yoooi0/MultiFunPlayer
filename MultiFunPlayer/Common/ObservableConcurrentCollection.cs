using PropertyChanged;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MultiFunPlayer.Common;

[DoNotNotify]
public class ObservableConcurrentCollection<T> : IList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly SynchronizationContext _context;
    private readonly List<T> _items;
    private readonly object _syncRoot;

    public ObservableConcurrentCollection()
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = new List<T>();
        _syncRoot = new object();
    }

    public ObservableConcurrentCollection(IEnumerable<T> elements)
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = new List<T>(elements);
        _syncRoot = new object();
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyObserversOfChange()
    {
        _context.Post(_ =>
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }, null);
    }

    public void Refresh() => NotifyObserversOfChange();

    public int Count
    {
        get { lock (_syncRoot) { return _items.Count; } }
    }

    public T this[int index]
    {
        get
        {
            lock (_syncRoot) { return _items[index]; }
        }
        set
        {
            lock (_syncRoot) { TrySetItemInternal(index, value); }
        }
    }

    public void Add(T item) { lock (_syncRoot) { AddItemInternal(item); } }
    public void AddRange(IEnumerable<T> items)
    {
        lock (_syncRoot)
        {
            foreach (var item in items)
                AddItemInternal(item);
        }
    }

    public void Clear() { lock (_syncRoot) { ClearItems(); } }
    public void CopyTo(T[] array, int index) { lock (_syncRoot) { _items.CopyTo(array, index); } }
    public bool Contains(T item) { lock (_syncRoot) { return _items.Contains(item); } }
    public int IndexOf(T item) { lock (_syncRoot) { return _items.IndexOf(item); } }

    IEnumerator IEnumerable.GetEnumerator() => ((IList<T>)this).GetEnumerator();
    public IEnumerator<T> GetEnumerator() 
    {
        lock (_syncRoot) 
        { 
            foreach (var item in _items)
                yield return item;
        } 
    }

    public void Insert(int index, T item) => _ = TryInsert(index, item);
    public bool TryInsert(int index, T item)
    {
        lock (_syncRoot)
        {
            return TryInsertInternal(index, item);
        }
    }

    public void Move(int oldIndex, int newIndex) => _ = TryMove(oldIndex, newIndex);
    public bool TryMove(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return false;

        lock (_syncRoot)
        {
            if (oldIndex < 0 || oldIndex >= _items.Count)
                return false;

            if (newIndex < 0 || newIndex > _items.Count)
                return false;

            T movingItem = _items[oldIndex];
            TryRemoveItemInternal(oldIndex);
            TryInsertInternal(newIndex, movingItem);
            NotifyObserversOfChange();

            return true;
        }
    }

    public bool Remove(T item)
    {
        lock (_syncRoot)
        {
            return TryRemoveItemInternal(_items.IndexOf(item));
        }
    }

    public void RemoveAt(int index) => _ = TryRemoveAt(index);
    public bool TryRemoveAt(int index)
    {
        lock (_syncRoot)
        {
            return TryRemoveItemInternal(index);
        }
    }

    protected virtual void ClearItems()
    {
        _items.Clear();
        NotifyObserversOfChange();
    }

    protected virtual void AddItemInternal(T item)
    {
        _items.Add(item);
        NotifyObserversOfChange();
    }

    protected virtual bool TryInsertInternal(int index, T item)
    {
        if (index < 0 || index > _items.Count)
            return false;

        _items.Insert(index, item);
        NotifyObserversOfChange();
        return true;
    }

    protected virtual bool TryRemoveItemInternal(int index)
    {
        if (index < 0 || index >= _items.Count)
            return false;

        _items.RemoveAt(index);
        NotifyObserversOfChange();
        return true;
    }

    protected virtual bool TrySetItemInternal(int index, T item)
    {
        if (index < 0 || index >= _items.Count)
            return false;

        _items[index] = item;
        NotifyObserversOfChange();
        return true;
    }

    bool ICollection<T>.IsReadOnly => false;
}