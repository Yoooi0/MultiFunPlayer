using Linearstar.Windows.RawInput;
using MultiFunPlayer.Common.Input.Gesture;
using MultiFunPlayer.Common.Input.Processor;
using NLog;
using Stylet;
using StyletIoC;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutManager : IDisposable
    {
        event EventHandler<IInputGesture> OnGesture;

        IReadOnlyCollection<string> Actions { get; }
        IReadOnlyCollection<string> AxisActions { get; }
        IReadOnlyDictionary<IInputGesture, string> Shortcuts { get; }

        void RegisterWindow(HwndSource source);
        void RegisterAction(string name, Action command);
        void RegisterAction(string name, Action<float, float> command);
        void RegisterShortcut(IInputGesture gesture, string commandName);
        void RemoveShortcut(IInputGesture gesture);
    }

    public class ShortcutManager : IShortcutManager
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Action> _actions;
        private readonly Dictionary<string, Action<float, float>> _axisActions;
        private readonly ObservableConcurrentDictionary<IInputGesture, string> _shortcuts;

        private readonly IReadOnlyList<IInputProcessor> _processors;

        private HwndSource _source;

        public event EventHandler<IInputGesture> OnGesture;

        public IReadOnlyCollection<string> Actions => _actions.Keys;
        public IReadOnlyCollection<string> AxisActions => _axisActions.Keys;
        public IReadOnlyDictionary<IInputGesture, string> Shortcuts => _shortcuts;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _processors = processors.ToList();

            _actions = new Dictionary<string, Action>();
            _axisActions = new Dictionary<string, Action<float, float>>();
            _shortcuts = new ObservableConcurrentDictionary<IInputGesture, string>();
        }

        public void RegisterWindow(HwndSource source)
        {
            if (_source != null)
                throw new Exception();

            _source = source;

            source.AddHook(MessageSink);

            RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.GamePad, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.Joystick, RawInputDeviceFlags.ExInputSink, source.Handle);
        }

        public void RegisterAction(string name, Action command)
        {
            if (_actions.ContainsKey(name))
                throw new NotSupportedException($"Cannot add more than one command with \"{name}\" name");

            Logger.Debug($"Registered \"{name}\" command");
            _actions[name] = command;
        }

        public void RegisterAction(string name, Action<float, float> command)
        {
            if (_axisActions.ContainsKey(name))
                throw new NotSupportedException($"Cannot add more than one command with \"{name}\" name");

            Logger.Debug($"Registered \"{name}\" command");
            _axisActions[name] = command;
        }

        public void RegisterShortcut(IInputGesture gesture, string commandName) => _shortcuts.Add(gesture, commandName);
        public void RemoveShortcut(IInputGesture gesture) => _shortcuts.Remove(gesture, out var _);

        private IntPtr MessageSink(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_INPUT = 0x00FF;

            if (msg == WM_INPUT)
            {
                var data = RawInputData.FromHandle(lParam);
                foreach (var gesture in _processors.SelectMany(p => p.GetGestures(data)))
                {
                    OnGesture?.Invoke(this, gesture);
                    if (!Shortcuts.TryGetValue(gesture, out var actionName))
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
