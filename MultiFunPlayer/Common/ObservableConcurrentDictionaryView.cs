using Microsoft.Xaml.Behaviors.Core;
using PropertyChanged;
using Stylet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Text;
using System.Linq;
using System.Linq.Expressions;

namespace MultiFunPlayer.Common
{
    [DoNotNotify]
    public class ObservableConcurrentDictionaryView<TKey, TValue, TView> : INotifyCollectionChanged, INotifyPropertyChanged, IReadOnlyDictionary<TKey, TView> where TValue : class
    {
        private readonly ObservableConcurrentDictionary<TKey, TValue> _dictionary;
        private readonly Func<TValue, TView> _selector;
        private readonly ObservableConcurrentDictionary<TKey, TView> _view;
        private readonly string _propertyName;

        public ObservableConcurrentDictionaryView(ObservableConcurrentDictionary<TKey, TValue> dictionary, Expression<Func<TValue, TView>> selector)
        {
            _propertyName = selector.NameForProperty();
            _selector = selector.Compile();

            _view = new ObservableConcurrentDictionary<TKey, TView>();
            _view.PropertyChanged += (s, e) => PropertyChanged?.Invoke(s, e);
            _view.CollectionChanged += (s, e) => CollectionChanged?.Invoke(s, e);

            _dictionary = dictionary;
            _dictionary.CollectionChanged += OnSourceCollectionChanged;
            _dictionary.PropertyChanged += OnSourceItemPropertyChanged;

            OnSourceCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, _dictionary.ToList()));
        }

        private void OnSourceItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not TValue target)
                return;

            if (!string.Equals(e.PropertyName, _propertyName, StringComparison.OrdinalIgnoreCase))
                return;

            foreach( var (key, value) in _dictionary)
            {
                if (value != target)
                    continue;

                _view[key] = _selector(value);
                return;
            }

            throw new KeyNotFoundException();
        }

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
                if (_view.ContainsKey(key))
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
            else if(e.Action == NotifyCollectionChangedAction.Remove)
            {
                RemoveRange(e.OldItems);
            }
            else if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                RemoveRange(e.OldItems);
                AddRange(e.NewItems);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var (key, value) in _dictionary)
                    Remove(key, _dictionary[key]);

                (_view as ICollection<KeyValuePair<TKey, TView>>).Clear();
            }
            else if (e.Action == NotifyCollectionChangedAction.Move)
            { }
        }

        #region IReadOnlyDictionary Members
        public TView this[TKey key] => _view[key];

        public IEnumerable<TKey> Keys => _dictionary.Keys;
        public IEnumerable<TView> Values => _view.Values;

        public int Count => (_dictionary as ICollection<KeyValuePair<TKey, TValue>>).Count;

        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public IEnumerator<KeyValuePair<TKey, TView>> GetEnumerator() => (_view as IEnumerable<KeyValuePair<TKey, TView>>).GetEnumerator();
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TView value) => _view.TryGetValue(key, out value);
        IEnumerator IEnumerable.GetEnumerator() => (_view as IEnumerable).GetEnumerator();
        #endregion

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
