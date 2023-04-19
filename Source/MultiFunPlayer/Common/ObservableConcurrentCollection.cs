using PropertyChanged;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

namespace MultiFunPlayer.Common;

public interface IReadOnlyObservableConcurrentCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    public int IndexOf(T item);
}

[DoNotNotify]
public class ObservableConcurrentCollection<T> : IList<T>, IReadOnlyObservableConcurrentCollection<T>, IList
{
    private readonly SynchronizationContext _context;
    private readonly List<T> _items;

    public object SyncRoot { get; } = new object();

    public ObservableConcurrentCollection()
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = new List<T>();
        BindingOperations.EnableCollectionSynchronization(this, SyncRoot);
    }

    public ObservableConcurrentCollection(IEnumerable<T> elements)
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = new List<T>(elements);
        BindingOperations.EnableCollectionSynchronization(this, SyncRoot);
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyObserversOfChange(NotifyCollectionChangedEventArgs collectionChangedEventArgs)
    {
        _context.Send(_ =>
        {
            CollectionChanged?.Invoke(this, collectionChangedEventArgs);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }, null);
    }

    private void NotifyObserversOfChange(NotifyCollectionChangedAction action, T value, int index)
        => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(action, value, index));
    private void NotifyObserversOfChange(NotifyCollectionChangedAction action, T value, int index, int oldIndex)
        => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(action, value, index, oldIndex));
    private void NotifyObserversOfChange(NotifyCollectionChangedAction action, T newValue, T oldValue, int index)
        => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(action, newValue, oldValue, index));

    public void Refresh() => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    public int Count
    {
        get { lock (SyncRoot) { return _items.Count; } }
    }

    public T this[int index]
    {
        get
        {
            lock (SyncRoot) { return _items[index]; }
        }
        set
        {
            lock (SyncRoot) { TrySetItemInternal(index, value); }
        }
    }

    public void Add(T item) { lock (SyncRoot) { AddItemInternal(item); } }
    public void AddRange(IEnumerable<T> items)
    {
        lock (SyncRoot)
        {
            foreach (var item in items)
                AddItemInternal(item);
        }
    }

    public void Clear() { lock (SyncRoot) { ClearItems(); } }
    public void CopyTo(T[] array, int index) { lock (SyncRoot) { _items.CopyTo(array, index); } }
    public bool Contains(T item) { lock (SyncRoot) { return _items.Contains(item); } }
    public int IndexOf(T item) { lock (SyncRoot) { return _items.IndexOf(item); } }

    IEnumerator IEnumerable.GetEnumerator() => ((IList<T>)this).GetEnumerator();
    public IEnumerator<T> GetEnumerator()
    {
        lock (SyncRoot)
        {
            foreach (var item in _items)
                yield return item;
        }
    }

    public void Insert(int index, T item) => _ = TryInsert(index, item);
    public bool TryInsert(int index, T item)
    {
        lock (SyncRoot)
        {
            return TryInsertInternal(index, item);
        }
    }

    public void Move(int oldIndex, int newIndex) => _ = TryMove(oldIndex, newIndex);
    public bool TryMove(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return false;

        lock (SyncRoot)
        {
            return TryMoveInternal(oldIndex, newIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (SyncRoot)
        {
            return TryRemoveItemInternal(_items.IndexOf(item));
        }
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        lock (SyncRoot)
        {
            foreach (var item in items)
                TryRemoveItemInternal(_items.IndexOf(item));
        }
    }

    public void RemoveAt(int index) => _ = TryRemoveAt(index);
    public bool TryRemoveAt(int index)
    {
        lock (SyncRoot)
        {
            return TryRemoveItemInternal(index);
        }
    }

    protected virtual void ClearItems()
    {
        lock (SyncRoot)
        {
            while (_items.Count > 0)
                TryRemoveItemInternal(0);
        }
    }

    protected virtual void AddItemInternal(T item)
    {
        _items.Add(item);
        NotifyObserversOfChange(NotifyCollectionChangedAction.Add, item, _items.Count - 1);
    }

    protected virtual bool TryMoveInternal(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _items.Count)
            return false;

        if (newIndex < 0 || newIndex > _items.Count)
            return false;

        T item = _items[oldIndex];
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        NotifyObserversOfChange(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex);
        return true;
    }

    protected virtual bool TryInsertInternal(int index, T item)
    {
        if (index < 0 || index > _items.Count)
            return false;

        _items.Insert(index, item);
        NotifyObserversOfChange(NotifyCollectionChangedAction.Add, item, index);
        return true;
    }

    protected virtual bool TryRemoveItemInternal(int index)
    {
        if (index < 0 || index >= _items.Count)
            return false;

        var item = _items[index];
        _items.RemoveAt(index);
        NotifyObserversOfChange(NotifyCollectionChangedAction.Remove, item, index);
        return true;
    }

    protected virtual bool TrySetItemInternal(int index, T item)
    {
        if (index < 0 || index >= _items.Count)
            return false;

        var oldItem = _items[index];
        _items[index] = item;
        NotifyObserversOfChange(NotifyCollectionChangedAction.Replace, item, oldItem, index);
        return true;
    }

    int IList.Add(object value)
    {
        lock (SyncRoot)
        {
            AddItemInternal((T)value);
            return _items.Count - 1;
        }
    }

    bool IList.Contains(object value) => Contains((T)value);
    int IList.IndexOf(object value) => IndexOf((T)value);
    void IList.Insert(int index, object value) => Insert(index, (T)value);
    void IList.Remove(object value) => Remove((T)value);

    void ICollection.CopyTo(Array array, int index)
    {
        lock (SyncRoot)
        {
            Array.Copy(_items.ToArray(), 0, array, index, Count);
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    public bool IsFixedSize => false;
    public bool IsReadOnly => false;
    public bool IsSynchronized => true;

    object IList.this[int index] { get => this[index]; set => this[index] = (T)value; }
}