using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace MultiFunPlayer.Input.RawInput;

[DisplayName("RawInput")]
internal sealed class RawInputProcessorSettings : AbstractInputProcessorSettings
{
    public int VirtualMouseWidth { get; set; } = 500;
    public int VirtualMouseHeight { get; set; } = 500;
    public int VirtualWheelWidth { get; set; } = 10;
    public int VirtualWheelHeight { get; set; } = 20;
    public int CombineMouseAxisEventCount { get; set; } = 10;
}

internal sealed class RawInputProcessor : IInputProcessor, IHandle<WindowCreatedMessage>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly RawInputProcessorSettings _settings;
    private readonly HashSet<Key> _pressedKeys;
    private HwndSource _source;

    private int _mouseAxisEventCount;
    private double _mouseXAxis, _mouseYAxis;
    private double _mouseDeltaX, _mouseDeltaY;
    private double _mouseWheelAxis, _mouseHorizontalWheelAxis;
    private int _mouseLastAbsoluteX, _mouseLastAbsoluteY;

    public event EventHandler<IInputGesture> OnGesture;

    public RawInputProcessor(RawInputProcessorSettings settings, IEventAggregator eventAggregator)
    {
        _settings = settings;
        eventAggregator.Subscribe(this);

        _pressedKeys = [];
        _mouseXAxis = _mouseYAxis = 0.5;
        _mouseWheelAxis = _mouseHorizontalWheelAxis = 0.5;
        _mouseLastAbsoluteX = _mouseLastAbsoluteY = -1;
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
            _pressedKeys.Add(key);
            HandleGesture(KeyboardGesture.Create(_pressedKeys, true));
        }
        else
        {
            if (_pressedKeys.Count > 0)
                HandleGesture(KeyboardGesture.Create(_pressedKeys, false));
            _pressedKeys.Clear();
        }
    }

    public void ParseMouseGestures(RawInputMouseData data)
    {
        ParseMouseButtonGestures(data.Mouse);
        ParseMouseAxisGestures(data.Mouse);
    }

    private void ParseMouseButtonGestures(in RawMouse mouse)
    {
        var buttons = mouse.Buttons;
        if (buttons.HasFlag(RawMouseButtonFlags.Button4Up)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton1, false));
        else if (buttons.HasFlag(RawMouseButtonFlags.Button5Up)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton2, false));
        else if (buttons.HasFlag(RawMouseButtonFlags.LeftButtonUp)) HandleGesture(MouseButtonGesture.Create(MouseButton.Left, false));
        else if (buttons.HasFlag(RawMouseButtonFlags.RightButtonUp)) HandleGesture(MouseButtonGesture.Create(MouseButton.Right, false));
        else if (buttons.HasFlag(RawMouseButtonFlags.MiddleButtonUp)) HandleGesture(MouseButtonGesture.Create(MouseButton.Middle, false));

        if (buttons.HasFlag(RawMouseButtonFlags.Button4Down)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton1, true));
        else if (buttons.HasFlag(RawMouseButtonFlags.Button5Down)) HandleGesture(MouseButtonGesture.Create(MouseButton.XButton2, true));
        else if (buttons.HasFlag(RawMouseButtonFlags.LeftButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Left, true));
        else if (buttons.HasFlag(RawMouseButtonFlags.RightButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Right, true));
        else if (buttons.HasFlag(RawMouseButtonFlags.MiddleButtonDown)) HandleGesture(MouseButtonGesture.Create(MouseButton.Middle, true));
    }

    private void ParseMouseAxisGestures(in RawMouse mouse)
    {
        if (mouse.ButtonData != 0)
        {
            if (mouse.Buttons.HasFlag(RawMouseButtonFlags.MouseWheel))
            {
                var delta = Math.Clamp((double)mouse.ButtonData / (120 * _settings.VirtualWheelHeight), -1, 1);
                _mouseWheelAxis = MathUtils.Clamp01(_mouseWheelAxis + delta);
                HandleGesture(MouseAxisGesture.Create(MouseAxis.MouseWheel, _mouseWheelAxis, delta, 0));
            }
            else if (mouse.Buttons.HasFlag(RawMouseButtonFlags.MouseHorizontalWheel))
            {
                var delta = Math.Clamp((double)mouse.ButtonData / (120 * _settings.VirtualWheelWidth), -1, 1);
                _mouseHorizontalWheelAxis = MathUtils.Clamp01(_mouseHorizontalWheelAxis + delta);
                HandleGesture(MouseAxisGesture.Create(MouseAxis.MouseHorizontalWheel, _mouseHorizontalWheelAxis, delta, 0));
            }
        }

        var isAbsolute = mouse.Flags.HasFlag(RawMouseFlags.MoveAbsolute);
        var deltaX = Math.Clamp(isAbsolute switch
        {
            true when _mouseLastAbsoluteX >= 0 => (double)(mouse.LastX - _mouseLastAbsoluteX) / _settings.VirtualMouseWidth,
            false => (double)mouse.LastX / _settings.VirtualMouseWidth,
            _ => 0
        }, -1, 1);
        var deltaY = Math.Clamp(isAbsolute switch
        {
            true when _mouseLastAbsoluteY >= 0 => (double)(mouse.LastY - _mouseLastAbsoluteY) / _settings.VirtualMouseHeight,
            false => (double)mouse.LastY / _settings.VirtualMouseHeight,
            _ => 0
        }, -1, 1);

        _mouseLastAbsoluteX = isAbsolute ? mouse.LastX : -1;
        _mouseLastAbsoluteY = isAbsolute ? mouse.LastY : -1;

        _mouseXAxis = MathUtils.Clamp01(_mouseXAxis + deltaX);
        _mouseYAxis = MathUtils.Clamp01(_mouseXAxis + deltaY);

        _mouseDeltaX += deltaX;
        _mouseDeltaY += deltaY;

        if (_mouseAxisEventCount++ >= _settings.CombineMouseAxisEventCount)
        {
            if (Math.Abs(_mouseDeltaX) > 0)
                HandleGesture(MouseAxisGesture.Create(MouseAxis.X, _mouseXAxis, _mouseDeltaX, 0));

            if (Math.Abs(_mouseDeltaY) > 0)
                HandleGesture(MouseAxisGesture.Create(MouseAxis.Y, _mouseYAxis, _mouseDeltaY, 0));

            _mouseDeltaX = _mouseDeltaY = 0;
            _mouseAxisEventCount = 0;
        }
    }

    private void HandleGesture(IInputGesture gesture)
        => OnGesture?.Invoke(this, gesture);

    private void RegisterWindow(HwndSource source)
    {
        if (_source != null)
            throw new InvalidOperationException("Cannot register more than one window");

        _source = source;

        source.AddHook(MessageSink);

        RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink, source.Handle);
        RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink, source.Handle);
    }

    public void Handle(WindowCreatedMessage message)
        => RegisterWindow(PresentationSource.FromVisual(Application.Current.MainWindow) as HwndSource);

    private void Dispose(bool disposing)
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
