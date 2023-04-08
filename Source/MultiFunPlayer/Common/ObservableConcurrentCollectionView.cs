using PropertyChanged;
using Stylet;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq.Expressions;

namespace MultiFunPlayer.Common;

public class ObservableConcurrentCollectionView<TValue, TView> : IReadOnlyObservableConcurrentCollection<TView> where TValue : class
{
    private readonly IReadOnlyObservableConcurrentCollection<TValue> _collection;
    private readonly Func<TValue, TView> _selector;
    private readonly ObservableConcurrentCollection<TView> _view;
    private readonly string _propertyName;

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableConcurrentCollectionView(IReadOnlyObservableConcurrentCollection<TValue> collection, Expression<Func<TValue, TView>> selector)
        : this(collection, selector.Compile(), selector.NameForProperty()) { }

    public ObservableConcurrentCollectionView(IReadOnlyObservableConcurrentCollection<TValue> collection, Func<TValue, TView> selector, string propertyName)
    {
        _propertyName = propertyName;
        _selector = selector;

        _view = new ObservableConcurrentCollection<TView>();
        _view.PropertyChanged += (s, e) => PropertyChanged?.Invoke(s, e);
        _view.CollectionChanged += (s, e) => CollectionChanged?.Invoke(s, e);

        _collection = collection;
        _collection.CollectionChanged += OnSourceCollectionChanged;
        _collection.PropertyChanged += OnSourceItemPropertyChanged;

        OnSourceCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, _collection.ToList()));
    }

    [SuppressPropertyChangedWarnings]
    private void OnSourceItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not TValue target)
            return;

        if (!string.Equals(e.PropertyName, _propertyName, StringComparison.OrdinalIgnoreCase))
            return;

        var index = _collection.IndexOf(target);
        _view[index] = _selector(target);
    }

    [SuppressPropertyChangedWarnings]
    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        void RegisterPropertyChanged(TValue value)
        {
            if (value is not INotifyPropertyChanged o)
                return;

            o.PropertyChanged -= OnSourceItemPropertyChanged;
            o.PropertyChanged += OnSourceItemPropertyChanged;
        }

        void UnregisterPropertyChanged(TValue value)
        {
            if (value is not INotifyPropertyChanged o)
                return;

            o.PropertyChanged -= OnSourceItemPropertyChanged;
        }

        void Add(TValue value, int index)
        {
            if (index == _view.Count)
                _view.Add(_selector(value));
            else
                _view.Insert(index, _selector(value));

            RegisterPropertyChanged(value);
        }

        void Remove(TValue value, int index)
        {
            _view.RemoveAt(index);
            UnregisterPropertyChanged(value);
        }

        void Replace(TValue oldValue, TValue newValue, int index)
        {
            UnregisterPropertyChanged(oldValue);
            _view[index] = _selector(newValue);
            RegisterPropertyChanged(newValue);
        }

        void Move(int oldIndex, int newIndex) => _view.Move(oldIndex, newIndex);

        void AddRange(IEnumerable enumerable, int index)
        {
            foreach (var value in enumerable.OfType<TValue>())
                Add(value, index++);
        }

        void RemoveRange(IEnumerable enumerable, int index)
        {
            foreach (var value in enumerable.OfType<TValue>())
                Remove(value, index++);
        }

        if (e.Action == NotifyCollectionChangedAction.Add)
            AddRange(e.NewItems, e.NewStartingIndex);
        else if (e.Action == NotifyCollectionChangedAction.Remove)
            RemoveRange(e.OldItems, e.OldStartingIndex);
        else if (e.Action == NotifyCollectionChangedAction.Replace)
            Replace(e.OldItems[0] as TValue, e.NewItems[0] as TValue, e.NewStartingIndex);
        else if (e.Action == NotifyCollectionChangedAction.Move)
            Move(e.OldStartingIndex, e.NewStartingIndex);
    }

    public int IndexOf(TView item) => _view.IndexOf(item);

    #region IReadOnlyList
    public int Count => _view.Count;
    public TView this[int index] => _view[index];

    public IEnumerator<TView> GetEnumerator() => _view.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _view.GetEnumerator();
    #endregion
}
