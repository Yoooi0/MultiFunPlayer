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

public class MouseAxisGesture(MouseAxisGestureDescriptor descriptor, double value, double delta, double deltaTime) : IAxisInputGesture
{
    public double Value { get; } = value;
    public double Delta { get; } = delta;
    public double DeltaTime { get; } = deltaTime;
    public MouseAxis Axis => descriptor.Axis;

    public IInputGestureDescriptor Descriptor => descriptor;

    public override string ToString() => $"[Mouse Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

    public static MouseAxisGesture Create(MouseAxis axis, double value, double delta, double deltaTime) => new(new(axis), value, delta, deltaTime);
}
