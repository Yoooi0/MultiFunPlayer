namespace MultiFunPlayer.Input.RawInput
{
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

        public float Value { get; }
        public float Delta { get; }
        public MouseAxis Axis => _descriptor.Axis;

        public IInputGestureDescriptor Descriptor => _descriptor;

        public MouseAxisGesture(MouseAxisGestureDescriptor descriptor, float value, float delta)
        {
            _descriptor = descriptor;

            Value = value;
            Delta = delta;
        }

        public override string ToString() => $"[Mouse Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

        public static MouseAxisGesture Create(MouseAxis axis, float value, float delta) => new(new(axis), value, delta);
    }
}
