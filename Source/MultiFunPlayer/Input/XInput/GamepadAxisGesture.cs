namespace MultiFunPlayer.Input.XInput;

internal enum GamepadAxis
{
    LeftTrigger,
    RightTrigger,
    LeftThumbX,
    LeftThumbY,
    RightThumbX,
    RightThumbY
}

internal sealed record GamepadAxisGestureDescriptor(int UserIndex, GamepadAxis Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}]";
}

internal sealed class GamepadAxisGesture(GamepadAxisGestureDescriptor descriptor, double value, double delta, double deltaTime) : AbstractAxisInputGesture(descriptor, value, delta, deltaTime)
{
    public int UserIndex => descriptor.UserIndex;
    public GamepadAxis Axis => descriptor.Axis;

    public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}, Value: {Value}, Delta: {Delta}]";

    public static GamepadAxisGesture Create(int userIndex, GamepadAxis axis, double value, double delta, double deltaTime) => new(new(userIndex, axis), value, delta, deltaTime);
}
