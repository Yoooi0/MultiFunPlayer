using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common;
using NLog;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Interop;

namespace MultiFunPlayer.Input.RawInput;

public class RawInputProcessor : IInputProcessor
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<Key, bool> _keyboardState;
    private HwndSource _source;

    private long _lastMouseAxisTimestamp;
    private double _mouseXAxis, _mouseYAxis;
    private double _lastMouseXAxis, _lastMouseYAxis;
    private double _mouseWheelAxis, _mouseHorizontalWheelAxis;

    public event EventHandler<IInputGesture> OnGesture;

    public RawInputProcessor()
    {
        _keyboardState = new Dictionary<Key, bool>();
        _lastMouseAxisTimestamp = 0;
        _mouseXAxis = _mouseYAxis = 0.5;
        _lastMouseXAxis = _lastMouseYAxis = 0.5;
        _mouseWheelAxis = _mouseHorizontalWheelAxis = 0.5;
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

            switch (data)
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

        if (data.Mouse.ButtonData != 0)
        {
            var delta = data.Mouse.ButtonData / (120d * 50d);
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

        _mouseXAxis = MathUtils.Clamp01(_mouseXAxis + data.Mouse.LastX / 500d);
        _mouseYAxis = MathUtils.Clamp01(_mouseYAxis + data.Mouse.LastY / 500d);

        var timestamp = Stopwatch.GetTimestamp();
        if ((timestamp - _lastMouseAxisTimestamp) / (double)Stopwatch.Frequency >= 0.01)
        {
            _lastMouseAxisTimestamp = timestamp;

            var deltaX = _mouseXAxis - _lastMouseXAxis;
            var deltaY = _mouseYAxis - _lastMouseYAxis;
            if (Math.Abs(deltaX) > 0.000001)
            {
                HandleGesture(MouseAxisGesture.Create(MouseAxis.X, _mouseXAxis, deltaX));
                _lastMouseXAxis = _mouseXAxis;
            }

            if (Math.Abs(deltaY) > 0.000001)
            {
                HandleGesture(MouseAxisGesture.Create(MouseAxis.Y, _mouseYAxis, deltaY));
                _lastMouseYAxis = _mouseYAxis;
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
