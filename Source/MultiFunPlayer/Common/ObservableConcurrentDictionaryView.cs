﻿using PropertyChanged;
using Stylet;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace MultiFunPlayer.Common;

[DoNotNotify]
public class ObservableConcurrentDictionaryView<TKey, TValue, TView> : IReadOnlyObservableConcurrentDictionary<TKey, TView> where TValue : class
{
    private readonly IReadOnlyObservableConcurrentDictionary<TKey, TValue> _dictionary;
    private readonly Func<TValue, TView> _selector;
    private readonly ObservableConcurrentDictionary<TKey, TView> _view;
    private readonly string _propertyName;

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableConcurrentDictionaryView(IReadOnlyObservableConcurrentDictionary<TKey, TValue> dictionary, Expression<Func<TValue, TView>> selector)
        : this(dictionary, selector.Compile(), selector.NameForProperty()) { }

    public ObservableConcurrentDictionaryView(IReadOnlyObservableConcurrentDictionary<TKey, TValue> dictionary, Func<TValue, TView> selector, string propertyName)
    {
        _propertyName = propertyName;
        _selector = selector;

        _view = [];
        _view.PropertyChanged += (s, e) => PropertyChanged?.Invoke(s, e);
        _view.CollectionChanged += (s, e) => CollectionChanged?.Invoke(s, e);

        _dictionary = dictionary;
        _dictionary.CollectionChanged += OnSourceCollectionChanged;
        _dictionary.PropertyChanged += OnSourceItemPropertyChanged;

        OnSourceCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, _dictionary.ToList()));
    }

    [SuppressPropertyChangedWarnings]
    private void OnSourceItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not TValue target)
            return;

        if (!string.Equals(e.PropertyName, _propertyName, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var (key, value) in _dictionary)
        {
            if (value != target)
                continue;

            _view[key] = _selector(value);
            return;
        }

        throw new KeyNotFoundException();
    }

    [SuppressPropertyChangedWarnings]
    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(TKey key, TValue value)
        {
            _view[key] = _selector(value);
            if (value is INotifyPropertyChanged o)
            {
                o.PropertyChanged -= OnSourceItemPropertyChanged;
                o.PropertyChanged += OnSourceItemPropertyChanged;
            }
        }

        void AddRange(IEnumerable enumerable)
        {
            foreach (var (key, value) in enumerable.OfType<KeyValuePair<TKey, TValue>>())
                Add(key, value);
        }

        void Remove(TKey key, TValue value)
        {
            _view.Remove(key);
            if (value is INotifyPropertyChanged o)
                o.PropertyChanged -= OnSourceItemPropertyChanged;
        }

        void RemoveRange(IEnumerable enumerable)
        {
            foreach (var (key, value) in enumerable.OfType<KeyValuePair<TKey, TValue>>())
                Remove(key, value);
        }

        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            AddRange(e.NewItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            RemoveRange(e.OldItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            RemoveRange(e.OldItems);
            AddRange(e.NewItems);
        }
    }

    #region IReadOnlyDictionary Members
    public TView this[TKey key] => _view[key];
    public IEnumerable<TKey> Keys => _dictionary.Keys;
    public IEnumerable<TView> Values => _view.Values;
    public int Count => _dictionary.Count;
    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
    public IEnumerator<KeyValuePair<TKey, TView>> GetEnumerator() => _view.GetEnumerator();
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TView value) => _view.TryGetValue(key, out value);
    IEnumerator IEnumerable.GetEnumerator() => (_view as IEnumerable)?.GetEnumerator();
    #endregion
}
