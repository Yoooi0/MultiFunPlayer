using System;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class MouseButtonGesture : IInputGesture
    {
        public MouseButton Button { get; }

        public MouseButtonGesture(MouseButton button) => Button = button;

        public override bool Equals(object other) => Equals(other as IInputGesture);
        public bool Equals(IInputGesture other) => other is MouseButtonGesture g && g.Button == Button;
        public override int GetHashCode() => HashCode.Combine(Button);
        public override string ToString() => $"[Mouse Button: {Button}]";
    }
}
