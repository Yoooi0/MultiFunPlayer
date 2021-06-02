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

        void RegisterWindow(HwndSource source);
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

        private HwndSource _source;

        public event EventHandler<IInputGesture> OnGesture;

        public IReadOnlyCollection<ShortcutActionDescriptor> Actions => _actions.Keys;
        public IReadOnlyDictionary<IInputGestureDescriptor, ShortcutActionDescriptor> Shortcuts => _shortcuts;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _processors = processors.ToList();

            _actions = new Dictionary<ShortcutActionDescriptor, IShortcutAction>();
            _shortcuts = new ObservableConcurrentDictionary<IInputGestureDescriptor, ShortcutActionDescriptor>();
        }

        public void RegisterWindow(HwndSource source)
        {
            if (_source != null)
                throw new InvalidOperationException("Cannot register more than one window");

            _source = source;

            source.AddHook(MessageSink);

            RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.GamePad, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.Joystick, RawInputDeviceFlags.ExInputSink, source.Handle);
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

        private IntPtr MessageSink(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_INPUT = 0x00FF;

            if (msg == WM_INPUT)
            {
                var data = RawInputData.FromHandle(lParam);
                Logger.Trace(data);

                foreach (var gesture in _processors.SelectMany(p => p.GetGestures(data)))
                {
                    OnGesture?.Invoke(this, gesture);
                    if (!Shortcuts.TryGetValue(gesture.Descriptor, out var actionDescriptor))
                        continue;

                    if (!_actions.TryGetValue(actionDescriptor, out var action))
                        continue;

                    action.Invoke(gesture);
                }
            }

            return IntPtr.Zero;
        }

        protected virtual void Dispose(bool disposing)
        {
            _source?.RemoveHook(MessageSink);

            RawInputDevice.UnregisterDevice(HidUsageAndPage.Keyboard);
            RawInputDevice.UnregisterDevice(HidUsageAndPage.Mouse);
            RawInputDevice.UnregisterDevice(HidUsageAndPage.GamePad);
            RawInputDevice.UnregisterDevice(HidUsageAndPage.Joystick);

            _source = null;
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
