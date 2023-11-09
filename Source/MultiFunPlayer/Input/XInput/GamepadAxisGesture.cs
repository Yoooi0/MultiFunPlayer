namespace MultiFunPlayer.Input.XInput;

public enum GamepadAxis
{
    LeftTrigger,
    RightTrigger,
    LeftThumbX,
    LeftThumbY,
    RightThumbX,
    RightThumbY
}

public record GamepadAxisGestureDescriptor(int UserIndex, GamepadAxis Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}]";
}

public class GamepadAxisGesture : IAxisInputGesture
{
    private readonly GamepadAxisGestureDescriptor _descriptor;

    public double Value { get; }
    public double Delta { get; }
    public double DeltaTime { get; }

    public int UserIndex => _descriptor.UserIndex;
    public GamepadAxis Axis => _descriptor.Axis;
    public IInputGestureDescriptor Descriptor => _descriptor;

    public GamepadAxisGesture(GamepadAxisGestureDescriptor descriptor, double value, double delta, double deltaTime)
    {
        _descriptor = descriptor;

        Value = value;
        Delta = delta;
        DeltaTime = deltaTime;
    }

    public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}, Value: {Value}, Delta: {Delta}]";

    public static GamepadAxisGesture Create(int userIndex, GamepadAxis axis, double value, double delta, double deltaTime) => new(new(userIndex, axis), value, delta, deltaTime);
}
