using MultiFunPlayer.Common;
using Newtonsoft.Json;
using NLog;
using System.Diagnostics;
using Vortice.XInput;

namespace MultiFunPlayer.Input.XInput;

internal class XInputProcessor : IInputProcessor
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly State[] _states;
    private readonly HashSet<GamepadVirtualKey> _pressedKeys;

    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    public event EventHandler<IInputGesture> OnGesture;

    public XInputProcessor()
    {
        _states = new State[4];
        _pressedKeys = new HashSet<GamepadVirtualKey>();
        _cancellationSource = new CancellationTokenSource();
        _thread = new Thread(() => Update(_cancellationSource.Token))
        {
            IsBackground = true
        };

        _thread.Start();
    }

    private void Update(CancellationToken token)
    {
        const int sleepMs = (int)(1000 / 30d);

        Vortice.XInput.XInput.SetReporting(true);

        for (var i = 0; i < _states.Length; i++)
        {
            if (!Vortice.XInput.XInput.GetCapabilities(i, DeviceQueryType.Any, out var capabilities))
                continue;

            Logger.Debug("User: {0}, Capabilities: {1}", i, JsonConvert.SerializeObject(capabilities, Formatting.None));
        }

        var stopwatch = Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            for (var i = 0; i < _states.Length; i++)
            {
                if (!Vortice.XInput.XInput.GetState(i, out var state))
                    continue;

                Logger.Trace("User: {0}, {1}, PacketNumber: {2}", i, state.Gamepad, state.PacketNumber);
                while (Vortice.XInput.XInput.GetKeystroke(i, out var keystroke))
                    ParseKeystrokeGestures(i, keystroke);

                var lastState = _states[i];
                if (lastState.PacketNumber == state.PacketNumber)
                    continue;

                ParseStateGestures(i, ref lastState.Gamepad, ref state.Gamepad, elapsed);
                _states[i] = state;
            }

            Thread.Sleep(sleepMs);
        }
    }

    private void ParseKeystrokeGestures(int userIndex, Keystroke keystroke)
    {
        Logger.Trace("User: {0}, Keystroke: {1}, Flags: {2}", userIndex, keystroke.VirtualKey, keystroke.Flags);

        if (keystroke.Flags == KeyStrokeFlags.KeyDown)
        {
            _pressedKeys.Add(keystroke.VirtualKey);
        }
        else if (keystroke.Flags == KeyStrokeFlags.KeyUp)
        {
            if (_pressedKeys.Count > 0)
                HandleGesture(GamepadButtonGesture.Create(userIndex, _pressedKeys));
            _pressedKeys.Clear();
        }
    }

    private void ParseStateGestures(int userIndex, ref Gamepad last, ref Gamepad current, double elapsed)
    {
        void CreateAxisGestureShort(short last, short current, GamepadAxis axis)
        {
            var delta = (double)(current - last) / ushort.MaxValue;
            var value = MathUtils.Map(current, short.MinValue, short.MaxValue, 0, 1);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, value, delta, elapsed));
        }

        void CreateAxisGestureByte(byte last, byte current, GamepadAxis axis)
        {
            var delta = (double)(current - last) / byte.MaxValue;
            var value = MathUtils.Map(current, byte.MinValue, byte.MaxValue, 0, 1);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, value, delta, elapsed));
        }

        if (current.RightThumbX != last.RightThumbX) CreateAxisGestureShort(last.RightThumbX, current.RightThumbX, GamepadAxis.RightThumbX);
        if (current.RightThumbY != last.RightThumbY) CreateAxisGestureShort(last.RightThumbY, current.RightThumbY, GamepadAxis.RightThumbY);

        if (current.LeftThumbX != last.LeftThumbX) CreateAxisGestureShort(last.LeftThumbX, current.LeftThumbX, GamepadAxis.LeftThumbX);
        if (current.LeftThumbY != last.LeftThumbY) CreateAxisGestureShort(last.LeftThumbY, current.LeftThumbY, GamepadAxis.LeftThumbY);

        if (current.RightTrigger != last.RightTrigger) CreateAxisGestureByte(last.RightTrigger, current.RightTrigger, GamepadAxis.RightTrigger);
        if (current.LeftTrigger != last.LeftTrigger) CreateAxisGestureByte(last.LeftTrigger, current.LeftTrigger, GamepadAxis.LeftTrigger);
    }

    private void HandleGesture(IInputGesture gesture)
        => OnGesture?.Invoke(this, gesture);

    protected virtual void Dispose(bool disposing)
    {
        Vortice.XInput.XInput.SetReporting(false);

        _cancellationSource?.Cancel();
        _thread?.Join();
        _cancellationSource?.Dispose();

        _thread = null;
        _cancellationSource = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
