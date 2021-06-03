using MultiFunPlayer.Common.Input.Gesture;
using System;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutAction
    {
        ShortcutActionDescriptor Descriptor { get; }
        void Invoke(IInputGesture gesture);
    }

    public class ShortcutAction : IShortcutAction
    {
        private readonly Action _action;

        public ShortcutActionDescriptor Descriptor { get; }

        public ShortcutAction(string name, Action action)
        {
            Descriptor = new ShortcutActionDescriptor(name, ShortcutActionType.Simple);
            _action = action;
        }

        public void Invoke(IInputGesture gesture) => _action?.Invoke();
        public override string ToString() => Descriptor.ToString();
    }

    public class AxisShortcutAction : IShortcutAction
    {
        private readonly Action<float, float> _action;

        public ShortcutActionDescriptor Descriptor { get; }

        public AxisShortcutAction(string name, Action<float, float> action)
        {
            Descriptor = new ShortcutActionDescriptor(name, ShortcutActionType.Axis);
            _action = action;
        }

        public void Invoke(IInputGesture gesture) => Invoke(gesture as IAxisInputGesture);
        public void Invoke(IAxisInputGesture gesture) => _action?.Invoke(gesture.Value, gesture.Delta);
        public override string ToString() => Descriptor.ToString();
    }
}
