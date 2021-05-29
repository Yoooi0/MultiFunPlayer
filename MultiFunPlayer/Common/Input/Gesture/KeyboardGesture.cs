using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class KeyboardGesture : IInputGesture
    {
        private static readonly IEqualityComparer<SortedSet<Key>> _comparer = SortedSet<Key>.CreateSetComparer();

        private readonly SortedSet<Key> _keys;

        public IReadOnlyCollection<Key> Keys => _keys;

        public KeyboardGesture(params Key[] keys) => _keys = new SortedSet<Key>(keys);
        public KeyboardGesture(IEnumerable<Key> keys) => _keys = new SortedSet<Key>(keys.ToHashSet());

        public override bool Equals(object other) => Equals(other as IInputGesture);
        public bool Equals(IInputGesture other) => other is KeyboardGesture g && _comparer.Equals(_keys, g._keys);
        public override int GetHashCode() => _comparer.GetHashCode(_keys);
        public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";
    }
}
