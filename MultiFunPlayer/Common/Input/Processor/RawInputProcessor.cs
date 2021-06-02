using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common.Input.Gesture;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;

namespace MultiFunPlayer.Common.Input.Processor
{
    public class RawInputProcessor : IInputProcessor
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Key, bool> _keyboardState;
        private HwndSource _source;

        private float _mouseXAxis, _mouseYAxis;
        private float _mouseWheelAxis, _mouseHorizontalWheelAxis;

        public event EventHandler<IInputGesture> OnGesture;

        public RawInputProcessor()
        {
            _keyboardState = new Dictionary<Key, bool>();
            _mouseXAxis = _mouseYAxis = 0.5f;
            _mouseWheelAxis = _mouseHorizontalWheelAxis = 0.5f;
        }

        public void RegisterWindow(HwndSource source)
        {
            if (_source != null)
                throw new InvalidOperationException("Cannot register more than one window");

            _source = source;

            source.AddHook(MessageSink);

            RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink, source.Handle);
            RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink, source.Handle);
        }

        private IntPtr MessageSink(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_INPUT = 0x00FF;

            if (msg == WM_INPUT)
            {
                var data = RawInputData.FromHandle(lParam);
                Logger.Trace(data);

                switch(data)
                {
                    case RawInputKeyboardData keyboard:
                        ParseKeyboardGestures(keyboard);
                        break;
                    case RawInputMouseData mouse:
                        ParseMouseGestures(mouse);
                        break;
                }
            }

            return IntPtr.Zero;
        }

        public void ParseKeyboardGestures(RawInputKeyboardData data)
        {
            var key = KeyInterop.KeyFromVirtualKey(data.Keyboard.VirutalKey);
            var pressed = (data.Keyboard.Flags & RawKeyboardFlags.Up) == 0;

            if (pressed)
            {
                _keyboardState[key] = true;
            }
            else
            {
                var pressedKeys = _keyboardState.Where(x => x.Value).Select(x => x.Key).ToList();
                if (pressedKeys.Count > 0)
                    HandleGesture(KeyboardGesture.Create(pressedKeys));

                foreach (var pressedKey in pressedKeys)
                    _keyboardState[pressedKey] = false;
            }
        }

        public void ParseMouseGestures(RawInputMouseData data)
        {
            bool HasFlag(RawMouseButtonFlags flag) => data.Mouse.Buttons.HasFlag(flag);

            if (HasFlag(RawMouseButtonFlags.Button4Down)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton1));
            else if (HasFlag(RawMouseButtonFlags.Button5Down)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton2));
            else if (HasFlag(RawMouseButtonFlags.LeftButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Left));
            else if (HasFlag(RawMouseButtonFlags.RightButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Right));
            else if (HasFlag(RawMouseButtonFlags.MiddleButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Middle));

            if (data.Mouse.LastX != 0)
            {
                var delta = data.Mouse.LastX / 500.0f;
                _mouseXAxis = MathUtils.Clamp01(_mouseXAxis + delta);
                HandleGesture(MouseAxisGesture.Create(MouseAxis.X, _mouseXAxis, delta));
            }

            if (data.Mouse.LastY != 0)
            {
                var delta = data.Mouse.LastY / 500.0f;
                _mouseYAxis = MathUtils.Clamp01(_mouseYAxis + delta);
                HandleGesture(MouseAxisGesture.Create(MouseAxis.Y, _mouseYAxis, delta));
            }

            if (data.Mouse.ButtonData != 0)
            {
                var delta = data.Mouse.ButtonData / (120.0f * 50.0f);
                if (HasFlag(RawMouseButtonFlags.MouseWheel))
                {
                    _mouseWheelAxis = MathUtils.Clamp01(_mouseWheelAxis + delta);
                    HandleGesture(MouseAxisGesture.Create(MouseAxis.MouseWheel, _mouseWheelAxis, delta));
                }
                else if (HasFlag(RawMouseButtonFlags.MouseHorizontalWheel))
                {
                    _mouseHorizontalWheelAxis = MathUtils.Clamp01(_mouseHorizontalWheelAxis + delta);
                    HandleGesture(MouseAxisGesture.Create(MouseAxis.MouseHorizontalWheel, _mouseHorizontalWheelAxis, delta));
                }
            }
        }

        private void HandleGesture(IInputGesture gesture)
            => OnGesture?.Invoke(this, gesture);

        protected virtual void Dispose(bool disposing)
        {
            _source?.RemoveHook(MessageSink);

            RawInputDevice.UnregisterDevice(HidUsageAndPage.Keyboard);
            RawInputDevice.UnregisterDevice(HidUsageAndPage.Mouse);

            _source = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
