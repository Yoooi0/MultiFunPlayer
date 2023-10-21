using Vortice.XInput;

namespace MultiFunPlayer.Input.XInput;

public record GamepadButtonGestureDescriptor : ISimpleInputGestureDescriptor
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

    public virtual bool Equals(GamepadButtonGestureDescriptor other) => UserIndex == other?.UserIndex && _comparer.Equals(_buttons, other?._buttons);
    public bool Equals(IInputGestureDescriptor other) => other is GamepadButtonGestureDescriptor d && Equals(d);
    public override int GetHashCode() => _comparer.GetHashCode(_buttons);
    public override string ToString() => $"[Gamepad Buttons: {UserIndex}/{Buttons}]";
}

public class GamepadButtonGesture : ISimpleInputGesture
{
    private readonly GamepadButtonGestureDescriptor _descriptor;

    public IInputGestureDescriptor Descriptor => _descriptor;
    public int UserIndex => _descriptor.UserIndex;
    public IEnumerable<GamepadVirtualKey> Buttons => _descriptor.Buttons;

    public GamepadButtonGesture(GamepadButtonGestureDescriptor descriptor) => _descriptor = descriptor;

    internal static GamepadButtonGesture Create(int userIndex, IEnumerable<GamepadVirtualKey> buttons) => new(new(userIndex, buttons));
}
