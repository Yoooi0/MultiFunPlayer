using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider
{
    public class MotionProviderCollection : Collection<IMotionProvider>
    {
        private readonly Dictionary<string, IMotionProvider> _dictionary;

        public IMotionProvider this[string key] => key != null && _dictionary != null && _dictionary.TryGetValue(key, out var item) ? item : null;

        public MotionProviderCollection()
        {
            _dictionary = new Dictionary<string, IMotionProvider>();

            var types = Assembly.GetExecutingAssembly()
                                .GetTypes()
                                .Where(t => t.IsClass && !t.IsAbstract)
                                .Where(t => typeof(IMotionProvider).IsAssignableFrom(t));

            foreach (var type in types)
                Add((IMotionProvider)Activator.CreateInstance(type));
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            _dictionary.Clear();
        }

        protected string GetKeyForItem(IMotionProvider item) => item.Name;

        protected override void InsertItem(int index, IMotionProvider item)
        {
            var key = GetKeyForItem(item);
            if (_dictionary.ContainsKey(key))
            {
                index = Items.IndexOf(_dictionary[key]);
                SetItem(index, item);
            } 
            else
            {
                SetKey(key, item);
                base.InsertItem(index, item);
            }
        }

        protected override void RemoveItem(int index)
        {
            RemoveKey(GetKeyForItem(Items[index]));
            base.RemoveItem(index);
        }

        protected override void SetItem(int index, IMotionProvider item)
        {
            var newKey = GetKeyForItem(item);
            var oldKey = GetKeyForItem(Items[index]);

            SetKey(newKey, item);
            if (!string.Equals(oldKey, newKey, StringComparison.InvariantCultureIgnoreCase))
                RemoveKey(oldKey);

            base.SetItem(index, item);
        }

        private void SetKey(string key, IMotionProvider item) => _dictionary[key] = item;
        private void RemoveKey(string key) => _dictionary.Remove(key);
    }
}
