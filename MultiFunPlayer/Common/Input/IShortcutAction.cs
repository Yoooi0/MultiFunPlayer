using System;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutAction
    {
        IShortcutActionDescriptor Descriptor { get; }
        void Invoke(IInputGesture gesture);
    }

    public interface ISimpleShortcutAction : IShortcutAction { }
    public interface IAxisShortcutAction : IShortcutAction { }

    public class SimpleShortcutAction : ISimpleShortcutAction
    {
        private readonly Action _action;

        public IShortcutActionDescriptor Descriptor { get; }

        public SimpleShortcutAction(string name, Action action)
        {
            Descriptor = new SimpleShortcutActionDescriptor(name);
            _action = action;
        }

        public void Invoke(IInputGesture gesture) => _action?.Invoke();
        public override string ToString() => Descriptor.ToString();
    }

    public class AxisShortcutAction : IAxisShortcutAction
    {
        private readonly Action<float, float> _action;

        public IShortcutActionDescriptor Descriptor { get; }

        public AxisShortcutAction(string name, Action<float, float> action)
        {
            Descriptor = new AxisShortcutActionDescriptor(name);
            _action = action;
        }

        public void Invoke(IInputGesture gesture) => Invoke(gesture as IAxisInputGesture);
        public void Invoke(IAxisInputGesture gesture) => _action?.Invoke(gesture.Value, gesture.Delta);
        public override string ToString() => Descriptor.ToString();
    }
}
