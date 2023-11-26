using Vortice.XInput;

namespace MultiFunPlayer.Input.XInput;

public sealed record GamepadButtonGestureDescriptor : ISimpleInputGestureDescriptor
{
    private static readonly IEqualityComparer<SortedSet<GamepadVirtualKey>> _comparer = SortedSet<GamepadVirtualKey>.CreateSetComparer();

    private readonly SortedSet<GamepadVirtualKey> _buttons;

    public int UserIndex { get; }
    public IReadOnlyCollection<GamepadVirtualKey> Buttons => _buttons;

    public GamepadButtonGestureDescriptor(int userIndex, IEnumerable<GamepadVirtualKey> buttons)
    {
        _buttons = new SortedSet<GamepadVirtualKey>(buttons.ToHashSet());
        UserIndex = userIndex;
    }

    public bool Equals(GamepadButtonGestureDescriptor other) => UserIndex == other?.UserIndex && _comparer.Equals(_buttons, other?._buttons);
    public override int GetHashCode() => _comparer.GetHashCode(_buttons);
    public override string ToString() => $"[Gamepad Buttons: {UserIndex}/{Buttons}]";
}

public sealed class GamepadButtonGesture(GamepadButtonGestureDescriptor descriptor) : AbstractSimpleInputGesture(descriptor)
{
    public int UserIndex => descriptor.UserIndex;
    public IEnumerable<GamepadVirtualKey> Buttons => descriptor.Buttons;

    public override string ToString() => $"[Gamepad Button: {UserIndex}/{string.Join(", ", Buttons)}]";

    internal static GamepadButtonGesture Create(int userIndex, IEnumerable<GamepadVirtualKey> buttons) => new(new(userIndex, buttons));
}
