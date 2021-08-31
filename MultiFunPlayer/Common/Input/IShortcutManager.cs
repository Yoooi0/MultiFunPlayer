using NLog;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiFunPlayer.Common.Input
{
    public class GestureEventArgs : EventArgs
    {
        public GestureEventArgs(IInputGesture gesture) => Gesture = gesture;

        public bool Handled { get; set; }
        public IInputGesture Gesture { get; }
    }

    public interface IShortcutManager : IDisposable
    {
        bool HandleGestures { get; set; }

        event EventHandler<GestureEventArgs> OnGesture;

        IReadOnlyDictionary<IShortcutActionDescriptor, IShortcutAction> Actions { get; }
        IReadOnlyDictionary<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>> Bindings { get; }

        void RegisterAction(IShortcutAction action);
        void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
        void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);

        void RegisterAction(string name, Action action) => RegisterAction(new SimpleShortcutAction(name, action));
        void RegisterAction(string name, Action<float, float> action) => RegisterAction(new AxisShortcutAction(name, action));

        void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
        void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
    }

    public class ShortcutManager : IShortcutManager
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<IShortcutActionDescriptor, IShortcutAction> _actions;
        private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>> _bindings;
        private readonly List<IInputProcessor> _processors;

        public event EventHandler<GestureEventArgs> OnGesture;

        public IReadOnlyDictionary<IShortcutActionDescriptor, IShortcutAction> Actions => _actions;
        public IReadOnlyDictionary<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>> Bindings => _bindings;

        public bool HandleGestures { get; set; } = true;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _actions = new Dictionary<IShortcutActionDescriptor, IShortcutAction>();
            _bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>>();

            _processors = processors.ToList();
            foreach (var processor in _processors)
                processor.OnGesture += HandleGesture;
        }

        public void RegisterAction(IShortcutAction action)
        {
            if (_actions.ContainsKey(action.Descriptor))
                throw new NotSupportedException($"Duplicate action found \"{action.Descriptor}\"");

            Logger.Trace($"Registered \"{action}\" action");
            _actions[action.Descriptor] = action;
        }

        public void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
        {
            RegisterGesture(gestureDescriptor);

            var actions = _bindings[gestureDescriptor];
            if (actions.Contains(actionDescriptor))
                return;

            actions.Add(actionDescriptor);
        }

        public void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
        {
            if (!_bindings.ContainsKey(gestureDescriptor))
                return;

            var actions = _bindings[gestureDescriptor];
            if (!actions.Contains(actionDescriptor))
                return;

            actions.Remove(actionDescriptor);
        }

        public void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
        {
            if (_bindings.ContainsKey(gestureDescriptor))
                return;

            _bindings.Add(gestureDescriptor, new BindableCollection<IShortcutActionDescriptor>());
        }

        public void UnregisterGesture(IInputGestureDescriptor gestureDescriptor)
        {
            if (!_bindings.ContainsKey(gestureDescriptor))
                return;

            _bindings.Remove(gestureDescriptor);
        }

        private void HandleGesture(object sender, IInputGesture gesture)
        {
            var eventArgs = new GestureEventArgs(gesture);
            OnGesture?.Invoke(this, eventArgs);
            if (eventArgs.Handled)
                return;

            if (!HandleGestures)
                return;

            if (_bindings.TryGetValue(gesture.Descriptor, out var actionDescriptors))
                foreach (var actionDescriptor in actionDescriptors)
                    if (_actions.TryGetValue(actionDescriptor, out var action))
                        action.Invoke(gesture);
        }

        protected virtual void Dispose(bool disposing)
        {
            foreach (var processor in _processors)
                processor.OnGesture -= HandleGesture;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
