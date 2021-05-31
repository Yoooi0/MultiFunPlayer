using System;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class MouseButtonGestureDescriptor : IInputGestureDescriptor
    {
        public MouseButton Button { get; }

        public MouseButtonGestureDescriptor(MouseButton button) => Button = button;

        public bool Equals(IInputGestureDescriptor other) => other is MouseButtonGestureDescriptor d && d.Button == Button;
        public override int GetHashCode() => HashCode.Combine(Button);
        public override string ToString() => $"[Mouse Button: {Button}]";
    }

    public class MouseButtonGesture : IInputGesture
    {
        private readonly MouseButtonGestureDescriptor _descriptor;

        public MouseButton Button { get; }
        public IInputGestureDescriptor Descriptor => _descriptor;

        public MouseButtonGesture(MouseButtonGestureDescriptor descriptor) => _descriptor = descriptor;

        public override string ToString() => $"[Mouse Button: {Button}]";

        public static MouseButtonGesture Create(MouseButton button)
            => new MouseButtonGesture(new MouseButtonGestureDescriptor(button));
    }
}
