using Linearstar.Windows.RawInput;
using MultiFunPlayer.Common.Input.Gesture;
using MultiFunPlayer.Common.Input.Processor;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutManager : IDisposable
    {
        event EventHandler<IInputGesture> OnGesture;

        IReadOnlyCollection<ShortcutActionDescriptor> Actions { get; }
        IReadOnlyDictionary<IInputGestureDescriptor, ShortcutActionDescriptor> Shortcuts { get; }

        void RegisterAction(IShortcutAction action);
        void RegisterShortcut(IInputGestureDescriptor descriptor, ShortcutActionDescriptor actionDescriptor);
        void RemoveShortcut(IInputGestureDescriptor descriptor);
    }

    public class ShortcutManager : IShortcutManager
    {
        protected static readonly  Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<ShortcutActionDescriptor, IShortcutAction> _actions;
        private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, ShortcutActionDescriptor> _shortcuts;

        private readonly IReadOnlyList<IInputProcessor> _processors;

        public event EventHandler<IInputGesture> OnGesture;

        public IReadOnlyCollection<ShortcutActionDescriptor> Actions => _actions.Keys;
        public IReadOnlyDictionary<IInputGestureDescriptor, ShortcutActionDescriptor> Shortcuts => _shortcuts;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _processors = processors.ToList();
            foreach (var processor in _processors)
                processor.OnGesture += HandleGesture;

            _actions = new Dictionary<ShortcutActionDescriptor, IShortcutAction>();
            _shortcuts = new ObservableConcurrentDictionary<IInputGestureDescriptor, ShortcutActionDescriptor>();
        }

        private void HandleGesture(object sender, IInputGesture e)
        {
            OnGesture?.Invoke(this, e);

            if (_shortcuts.TryGetValue(e.Descriptor, out var actionDescriptor)
             && _actions.TryGetValue(actionDescriptor, out var action))
                action.Invoke(e);
        }

        public void RegisterAction(IShortcutAction action)
        {
            if (_actions.ContainsKey(action.Descriptor))
                throw new NotSupportedException($"Duplicate action found \"{action.Descriptor}\"");

            Logger.Trace($"Registered \"{action}\" action");
            _actions[action.Descriptor] = action;
        }

        public void RegisterShortcut(IInputGestureDescriptor gestureDescriptor, ShortcutActionDescriptor actionDescriptor)
        {
            if (gestureDescriptor == null)
                return;

            Logger.Debug($"Registered \"{gestureDescriptor}\" to \"{actionDescriptor}\"");
            _shortcuts[gestureDescriptor] = actionDescriptor;
        }

        public void RemoveShortcut(IInputGestureDescriptor gestureDescriptor)
        {
            if (gestureDescriptor == null)
                return;

            Logger.Debug($"Removed \"{gestureDescriptor}\" action");
            _shortcuts.Remove(gestureDescriptor, out var _);
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

    public static class ShortcutManagerExtensions
    {
        public static void RegisterAction(this IShortcutManager shortcutManager, string name, Action action)
            => shortcutManager.RegisterAction(new ShortcutAction(name, action));

        public static void RegisterAction(this IShortcutManager shortcutManager, string name, Action<float, float> action)
            => shortcutManager.RegisterAction(new AxisShortcutAction(name, action));
    }
}
