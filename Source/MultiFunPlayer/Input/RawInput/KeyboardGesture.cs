﻿using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public record KeyboardGestureDescriptor : ISimpleInputGestureDescriptor
{
    private static readonly IEqualityComparer<SortedSet<Key>> _comparer = SortedSet<Key>.CreateSetComparer();

    private readonly SortedSet<Key> _keys;
    public IReadOnlyCollection<Key> Keys => _keys;

    public KeyboardGestureDescriptor(IEnumerable<Key> keys) => _keys = new SortedSet<Key>(keys.ToHashSet());

    public virtual bool Equals(KeyboardGestureDescriptor other) => _comparer.Equals(_keys, other?._keys);
    public bool Equals(IInputGestureDescriptor other) => other is KeyboardGestureDescriptor d && Equals(d);
    public override int GetHashCode() => _comparer.GetHashCode(_keys);
    public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";
}

public class KeyboardGesture(KeyboardGestureDescriptor descriptor) : ISimpleInputGesture
{
    public IReadOnlyCollection<Key> Keys => descriptor.Keys;
    public IInputGestureDescriptor Descriptor => descriptor;

    public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";

    public static KeyboardGesture Create(IEnumerable<Key> keys) => new(new(keys));
}
