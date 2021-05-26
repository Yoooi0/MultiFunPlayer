using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public enum MouseAxis
    {
        X,
        Y,
        MouseWheel,
        MouseHorizontalWheel
    }

    public class MouseAxisGesture : IAxisInputGesture
    {
        public float Value { get; }
        public float Delta { get; }
        public MouseAxis Axis { get; }

        public MouseAxisGesture(MouseAxis axis) : this(axis, 0.5f, 0f) { }
        public MouseAxisGesture(MouseAxis axis, float value, float delta)
        {
            Value = value;
            Delta = delta;
            Axis = axis;
        }

        public override bool Equals(object other) => Equals(other as IInputGesture);
        public bool Equals(IInputGesture other) => other is MouseAxisGesture g && Axis == g.Axis;
        public override int GetHashCode() => HashCode.Combine(Axis);
    }
}
