﻿using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class KeyboardGestureDescriptor : IInputGestureDescriptor
    {
        private static readonly IEqualityComparer<SortedSet<Key>> _comparer = SortedSet<Key>.CreateSetComparer();

        private readonly SortedSet<Key> _keys;
        public IReadOnlyCollection<Key> Keys => _keys;

        public KeyboardGestureDescriptor(IEnumerable<Key> keys) => _keys = new SortedSet<Key>(keys.ToHashSet());

        public bool Equals(IInputGestureDescriptor other) => other is KeyboardGestureDescriptor d && _comparer.Equals(_keys, d._keys);
        public override int GetHashCode() => _comparer.GetHashCode(_keys);
        public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";
    }

    public class KeyboardGesture : IInputGesture
    {
        private readonly KeyboardGestureDescriptor _descriptor;

        public IReadOnlyCollection<Key> Keys => _descriptor.Keys;
        public IInputGestureDescriptor Descriptor => _descriptor;

        public KeyboardGesture(KeyboardGestureDescriptor descriptor) => _descriptor = descriptor;

        public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";

        public static KeyboardGesture Create(IEnumerable<Key> keys) => new(new(keys));
    }
}
