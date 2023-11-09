namespace MultiFunPlayer.Input.RawInput;

public enum MouseAxis
{
    X,
    Y,
    MouseWheel,
    MouseHorizontalWheel
}

public record MouseAxisGestureDescriptor(MouseAxis Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Axis: {Axis}]";
}

public class MouseAxisGesture : IAxisInputGesture
{
    private readonly MouseAxisGestureDescriptor _descriptor;

    public double Value { get; }
    public double Delta { get; }
    public double DeltaTime { get; }
    public MouseAxis Axis => _descriptor.Axis;

    public IInputGestureDescriptor Descriptor => _descriptor;

    public MouseAxisGesture(MouseAxisGestureDescriptor descriptor, double value, double delta, double deltaTime)
    {
        _descriptor = descriptor;

        Value = value;
        Delta = delta;
        DeltaTime = deltaTime;
    }

    public override string ToString() => $"[Mouse Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

    public static MouseAxisGesture Create(MouseAxis axis, double value, double delta, double deltaTime) => new(new(axis), value, delta, deltaTime);
}
