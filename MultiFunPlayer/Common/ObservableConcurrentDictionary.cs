using PropertyChanged;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace MultiFunPlayer.Common
{
    [DoNotNotify]
    public class ObservableConcurrentDictionary<TKey, TValue> : IDictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private readonly SynchronizationContext _context;
        private readonly ConcurrentDictionary<TKey, TValue> _dictionary;

        public ObservableConcurrentDictionary()
        {
            _context = AsyncOperationManager.SynchronizationContext;
            _dictionary = new ConcurrentDictionary<TKey, TValue>();
        }

        public ObservableConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            _context = AsyncOperationManager.SynchronizationContext;
            _dictionary = new ConcurrentDictionary<TKey, TValue>(collection);
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyObserversOfChange(NotifyCollectionChangedEventArgs collectionChangedArgs)
        {
            _context.Send(_ =>
            {
                CollectionChanged?.Invoke(this, collectionChangedArgs);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Count"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Keys)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Values)));
            }, null);
        }

        private void NotifyObserversOfChange(NotifyCollectionChangedAction action, TKey key, TValue value, int index)
            => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(action, new KeyValuePair<TKey, TValue>(key, value), index));

        private void NotifyObserversOfChange(NotifyCollectionChangedAction action, TKey key, TValue oldValue, TValue newValue, int index)
            => NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(action, new KeyValuePair<TKey, TValue>(key, newValue), new KeyValuePair<TKey, TValue>(key, oldValue), index));

        private bool TryAddWithNotification(KeyValuePair<TKey, TValue> item) => TryAddWithNotification(item.Key, item.Value);

        private bool TryAddWithNotification(TKey key, TValue value)
        {
            var result = _dictionary.TryAdd(key, value);
            if (result)
                NotifyObserversOfChange(NotifyCollectionChangedAction.Add, key, value, IndexOf(key));
            return result;
        }

        private bool TryRemoveWithNotification(TKey key, out TValue value)
        {
            var index = IndexOf(key);
            var result = _dictionary.TryRemove(key, out value);
            if (result)
                NotifyObserversOfChange(NotifyCollectionChangedAction.Remove, key, value, index);
            return result;
        }

        private void UpdateWithNotification(TKey key, TValue value)
        {
            var updated = _dictionary.TryGetValue(key, out var oldValue);
            _dictionary[key] = value;

            if(!updated)
                NotifyObserversOfChange(NotifyCollectionChangedAction.Add, key, value, IndexOf(key));
            else
                NotifyObserversOfChange(NotifyCollectionChangedAction.Replace, key, oldValue, value, IndexOf(key));
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (ContainsKey(key))
                UpdateWithNotification(key, value);
            else
                TryAddWithNotification(key, value);
        }

        private int IndexOf(TKey key)
        {
            var index = -1;
            foreach(var current in _dictionary.Keys)
            {
                index++;
                if (EqualityComparer<TKey>.Default.Equals(current, key))
                    return index;
            }

            return -1;
        }

        #region ICollection<KeyValuePair<TKey,TValue>> Members
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => TryAddWithNotification(item);

        void ICollection<KeyValuePair<TKey, TValue>>.Clear()
        {
            _dictionary.Clear();
            NotifyObserversOfChange(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Contains(item);
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(array, arrayIndex);
        int ICollection<KeyValuePair<TKey, TValue>>.Count => _dictionary.Count;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).IsReadOnly;
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => TryRemoveWithNotification(item.Key, out _);
        #endregion

        #region IEnumerable<KeyValuePair<TKey,TValue>> Members
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => _dictionary.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _dictionary.GetEnumerator();
        #endregion

        #region IDictionary<TKey,TValue> Members
        public void Add(TKey key, TValue value) => TryAddWithNotification(key, value);
        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public ICollection<TKey> Keys => _dictionary.Keys;
        public bool Remove(TKey key) => TryRemoveWithNotification(key, out _);
        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
        public ICollection<TValue> Values => _dictionary.Values;

        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set => UpdateWithNotification(key, value);
        }
        #endregion
    }
}
