using PropertyChanged;
using Stylet;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq.Expressions;

namespace MultiFunPlayer.Common;

public sealed class ObservableConcurrentCollectionView<TValue, TView> : IReadOnlyObservableConcurrentCollection<TView> where TValue : class
{
    private readonly IReadOnlyObservableConcurrentCollection<TValue> _collection;
    private readonly Func<TValue, TView> _selector;
    private readonly ObservableConcurrentCollection<TView> _view;
    private readonly string _propertyName;
    private readonly List<TValue> _registeredItems;

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableConcurrentCollectionView(IReadOnlyObservableConcurrentCollection<TValue> collection, Expression<Func<TValue, TView>> selector)
        : this(collection, selector.Compile(), selector.NameForProperty()) { }

    public ObservableConcurrentCollectionView(IReadOnlyObservableConcurrentCollection<TValue> collection, Func<TValue, TView> selector, string propertyName)
    {
        _propertyName = propertyName;
        _selector = selector;

        _registeredItems = [];
        _view = [];
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
            _registeredItems.Add(value);
        }

        void UnregisterPropertyChanged(TValue value)
        {
            if (value is not INotifyPropertyChanged o)
                return;

            o.PropertyChanged -= OnSourceItemPropertyChanged;
            _registeredItems.Remove(value);
        }

        lock (_registeredItems)
        {
            var newItems = _collection.ToList();

            while(_registeredItems.Count > 0)
                UnregisterPropertyChanged(_registeredItems[0]);

            _view.Clear();
            foreach (var item in newItems)
                RegisterPropertyChanged(item);

            _view.AddRange(newItems.Select(i => _selector(i)));
        }
    }

    public int IndexOf(TView item) => _view.IndexOf(item);

    #region IReadOnlyList
    public int Count => _view.Count;
    public TView this[int index] => _view[index];

    public IEnumerator<TView> GetEnumerator() => _view.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _view.GetEnumerator();
    #endregion
}
