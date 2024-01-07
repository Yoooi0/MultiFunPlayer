using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public sealed record KeyboardGestureDescriptor : ISimpleInputGestureDescriptor
{
    private static readonly IEqualityComparer<SortedSet<Key>> _comparer = SortedSet<Key>.CreateSetComparer();

    private readonly SortedSet<Key> _keys;
    public IReadOnlyCollection<Key> Keys => _keys;

    public KeyboardGestureDescriptor(IEnumerable<Key> keys) => _keys = new SortedSet<Key>(keys.ToHashSet());

    public bool Equals(KeyboardGestureDescriptor other) => _comparer.Equals(_keys, other?._keys);
    public override int GetHashCode() => _comparer.GetHashCode(_keys);
    public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";
}

public sealed class KeyboardGesture(KeyboardGestureDescriptor descriptor, bool state) : AbstractSimpleInputGesture(descriptor, state)
{
    public IReadOnlyCollection<Key> Keys => descriptor.Keys;

    public override string ToString() => $"[Keyboard Keys: {string.Join(", ", Keys)}]";

    public static KeyboardGesture Create(IEnumerable<Key> keys, bool state) => new(new(keys), state);
}
