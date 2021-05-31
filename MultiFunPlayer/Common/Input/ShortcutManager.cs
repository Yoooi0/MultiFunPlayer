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

        IReadOnlyCollection<string> Actions { get; }
        IReadOnlyCollection<string> AxisActions { get; }
        IReadOnlyDictionary<IInputGestureDescriptor, string> Shortcuts { get; }

        void RegisterWindow(HwndSource source);
        void RegisterAction(string name, Action action);
        void RegisterAction(string name, Action<float, float> action);
        void RegisterShortcut(IInputGestureDescriptor descriptor, string actionName);
        void RemoveShortcut(IInputGestureDescriptor descriptor);
    }

    public class ShortcutManager : IShortcutManager
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Action> _actions;
        private readonly Dictionary<string, Action<float, float>> _axisActions;
        private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, string> _shortcuts;

        private readonly IReadOnlyList<IInputProcessor> _processors;

        private HwndSource _source;

        public event EventHandler<IInputGesture> OnGesture;

        public IReadOnlyCollection<string> Actions => _actions.Keys;
        public IReadOnlyCollection<string> AxisActions => _axisActions.Keys;
        public IReadOnlyDictionary<IInputGestureDescriptor, string> Shortcuts => _shortcuts;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _processors = processors.ToList();

            _actions = new Dictionary<string, Action>();
            _axisActions = new Dictionary<string, Action<float, float>>();
            _shortcuts = new ObservableConcurrentDictionary<IInputGestureDescriptor, string>();
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

        public void RegisterAction(string name, Action action)
        {
            if (_actions.ContainsKey(name))
                throw new NotSupportedException($"Cannot add more than one action with \"{name}\" name");

            Logger.Trace($"Registered \"{name}\" action");
            _actions[name] = action;
        }

        public void RegisterAction(string name, Action<float, float> action)
        {
            if (_axisActions.ContainsKey(name))
                throw new NotSupportedException($"Cannot add more than one action with \"{name}\" name");

            Logger.Trace($"Registered \"{name}\" action");
            _axisActions[name] = action;
        }

        public void RegisterShortcut(IInputGestureDescriptor descriptor, string actionName)
        {
            if (descriptor == null)
                return;

            Logger.Debug($"Registered \"{descriptor}\" to \"{actionName}\"");
            _shortcuts[descriptor] = actionName;
        }

        public void RemoveShortcut(IInputGestureDescriptor descriptor)
        {
            if (descriptor == null)
                return;

            Logger.Debug($"Removed \"{descriptor}\" action");
            _shortcuts.Remove(descriptor, out var _);
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
                    if (!Shortcuts.TryGetValue(gesture.Descriptor, out var actionName))
                        continue;

                    if(gesture is IAxisInputGesture axisGesture && _axisActions.TryGetValue(actionName, out var axisAction))
                        axisAction.Invoke(axisGesture.Value, axisGesture.Delta);
                    else if(_actions.TryGetValue(actionName, out var action))
                        action.Invoke();
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
}
