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
public sealed class ObservableConcurrentCollection<T> : IList<T>, IReadOnlyObservableConcurrentCollection<T>, IList
{
    private readonly SynchronizationContext _context;
    private readonly List<T> _items;

    public object SyncRoot { get; } = new object();

    public ObservableConcurrentCollection()
    {
        _context = AsyncOperationManager.SynchronizationContext;
        _items = [];
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
        get { lock (SyncRoot) { return _items.Count; } }
    }

    public T this[int index]
    {
        get { lock (SyncRoot) { return _items[index]; } }
        set { lock (SyncRoot) { SetItemInternal(index, value); } }
    }

    public void Add(T item)
    {
        lock (SyncRoot)
        {
            _items.Add(item);
            NotifyObserversOfChange();
        }
    }

    public void AddRange(IEnumerable<T> items)
    {
        lock (SyncRoot)
        {
            _items.AddRange(items);
            NotifyObserversOfChange();
        }
    }

    public void SetFrom(IEnumerable<T> items)
    {
        lock (SyncRoot)
        {
            _items.Clear();
            _items.AddRange(items);
            NotifyObserversOfChange();
        }
    }

    public void Clear()
    {
        lock (SyncRoot)
        {
            _items.Clear();
            NotifyObserversOfChange();
        }
    }

    public void Insert(int index, T item)
    {
        lock (SyncRoot)
        {
            if (index < 0 || index > _items.Count)
                throw new IndexOutOfRangeException();

            _items.Insert(index, item);
            NotifyObserversOfChange();
        }
    }

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return;

        lock (SyncRoot)
        {
            if (oldIndex < 0 || oldIndex >= _items.Count)
                throw new IndexOutOfRangeException();

            if (newIndex < 0 || newIndex > _items.Count)
                throw new IndexOutOfRangeException();

            var item = _items[oldIndex];
            _items.RemoveAt(oldIndex);
            _items.Insert(newIndex, item);
            NotifyObserversOfChange();
        }
    }

    public bool Remove(T item)
    {
        lock (SyncRoot)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
                return false;

            RemoveItemInternal(index);
            return true;
        }
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        lock (SyncRoot)
        {
            foreach (var item in items)
                RemoveItemInternal(_items.IndexOf(item));
        }
    }

    public void RemoveAt(int index) => RemoveItemInternal(index);

    private void RemoveItemInternal(int index)
    {
        if (index < 0 || index >= _items.Count)
            throw new IndexOutOfRangeException();

        _items.RemoveAt(index);
        NotifyObserversOfChange();
    }

    private void SetItemInternal(int index, T item)
    {
        if (index < 0 || index >= _items.Count)
            throw new IndexOutOfRangeException();

        _items[index] = item;
        NotifyObserversOfChange();
    }

    int IList.Add(object value)
    {
        lock (SyncRoot)
        {
            _items.Add((T)value);
            return _items.Count - 1;
        }
    }

    public void CopyTo(T[] array, int index) { lock (SyncRoot) { _items.CopyTo(array, index); } }
    public bool Contains(T item) { lock (SyncRoot) { return _items.Contains(item); } }
    public int IndexOf(T item) { lock (SyncRoot) { return _items.IndexOf(item); } }

    IEnumerator IEnumerable.GetEnumerator() => ((IList<T>)this).GetEnumerator();
    public IEnumerator<T> GetEnumerator() { lock (SyncRoot) { return _items.ToList().GetEnumerator(); } }

    bool IList.Contains(object value) => value is T x && Contains(x);
    int IList.IndexOf(object value) => value is T x ? IndexOf(x) : -1;
    void IList.Insert(int index, object value)
    {
        if (value is T x)
            Insert(index, x);
    }

    void IList.Remove(object value)
    {
        if (value is T x)
            Remove(x);
    }

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