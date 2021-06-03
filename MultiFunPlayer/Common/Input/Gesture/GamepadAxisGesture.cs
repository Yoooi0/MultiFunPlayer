namespace MultiFunPlayer.Common.Input.Gesture
{
    public enum GamepadAxis
    {
        LeftTrigger,
        RightTrigger,
        LeftThumbX,
        LeftThumbY,
        RightThumbX,
        RightThumbY
    }

    public record GamepadAxisGestureDescriptor(int UserIndex, GamepadAxis Axis) : IInputGestureDescriptor
    {
        public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}]";
    }

    public class GamepadAxisGesture : IAxisInputGesture
    {
        private readonly GamepadAxisGestureDescriptor _descriptor;

        public int UserIndex => _descriptor.UserIndex;
        public GamepadAxis Axis => _descriptor.Axis;

        public float Value { get; }
        public float Delta { get; }

        public IInputGestureDescriptor Descriptor => _descriptor;

        public GamepadAxisGesture(GamepadAxisGestureDescriptor descriptor, float value, float delta)
        {
            _descriptor = descriptor;

            Value = value;
            Delta = delta;
        }

        public override string ToString() => $"[Gamepad Axis: {UserIndex}/{Axis}, Value: {Value}, Delta: {Delta}]";

        public static GamepadAxisGesture Create(int userIndex, GamepadAxis axis, float value, float delta) => new(new(userIndex, axis), value, delta);
    }
}
