namespace MultiFunPlayer.Input.RawInput;

internal enum MouseAxis
{
    X,
    Y,
    MouseWheel,
    MouseHorizontalWheel
}

internal sealed record MouseAxisGestureDescriptor(MouseAxis Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Axis: {Axis}]";
}

internal sealed class MouseAxisGesture(MouseAxisGestureDescriptor descriptor, double value, double delta, double deltaTime) : AbstractAxisInputGesture(descriptor, value, delta, deltaTime)
{
    public MouseAxis Axis => descriptor.Axis;

    public override string ToString() => $"[Mouse Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

    public static MouseAxisGesture Create(MouseAxis axis, double value, double delta, double deltaTime) => new(new(axis), value, delta, deltaTime);
}
