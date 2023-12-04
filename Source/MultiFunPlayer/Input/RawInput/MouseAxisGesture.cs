namespace MultiFunPlayer.Input.RawInput;

public enum MouseAxis
{
    X,
    Y,
    MouseWheel,
    MouseHorizontalWheel
}

public sealed record MouseAxisGestureDescriptor(MouseAxis Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Axis: {Axis}]";
}

public sealed class MouseAxisGesture(MouseAxisGestureDescriptor descriptor, double value, double delta, double deltaTime) : AbstractAxisInputGesture(descriptor, value, delta, deltaTime)
{
    public MouseAxis Axis => descriptor.Axis;

    public override string ToString() => $"[Mouse Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

    public static MouseAxisGesture Create(MouseAxis axis, double value, double delta, double deltaTime) => new(new(axis), value, delta, deltaTime);
}
