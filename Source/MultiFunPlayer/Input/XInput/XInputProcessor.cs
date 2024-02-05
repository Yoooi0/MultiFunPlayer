using MultiFunPlayer.Common;
using Newtonsoft.Json;
using NLog;
using System.ComponentModel;
using System.Diagnostics;
using Vortice.XInput;

namespace MultiFunPlayer.Input.XInput;

[DisplayName("XInput")]
internal sealed class XInputProcessorSettings : AbstractInputProcessorSettings
{
    [JsonProperty] public double RightThumbDeadZone { get; set; } = Gamepad.RightThumbDeadZone / 32767d;
    [JsonProperty] public double LeftThumbDeadZone {get; set; } = Gamepad.LeftThumbDeadZone / 32767d;
    [JsonProperty] public double TriggerDeadZone { get; set; } = Gamepad.TriggerThreshold / 255d;
}

internal sealed class XInputProcessor : IInputProcessor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly XInputProcessorSettings _settings;
    private readonly State[] _states;
    private readonly HashSet<GamepadVirtualKey> _pressedKeys;

    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    public event EventHandler<IInputGesture> OnGesture;

    public XInputProcessor(XInputProcessorSettings settings)
    {
        _settings = settings;

        _states = new State[4];
        _pressedKeys = [];
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

        if (keystroke.Flags.HasFlag(KeyStrokeFlags.KeyDown) || keystroke.Flags.HasFlag(KeyStrokeFlags.Repeat))
        {
            _pressedKeys.Add(keystroke.VirtualKey);
            HandleGesture(GamepadButtonGesture.Create(userIndex, _pressedKeys, true));
        }
        else if (keystroke.Flags.HasFlag(KeyStrokeFlags.KeyUp))
        {
            if (_pressedKeys.Count > 0)
                HandleGesture(GamepadButtonGesture.Create(userIndex, _pressedKeys, false));
            _pressedKeys.Clear();
        }
    }

    private void ParseStateGestures(int userIndex, ref Gamepad last, ref Gamepad current, double elapsed)
    {
        void CreateAxisGestureShort(short last, short current, double deadZone, GamepadAxis axis)
        {
            if (current == last)
                return;
            if (Math.Abs(current) < deadZone && Math.Abs(last) < deadZone)
                return;

            var currentValue = UnLerpShort(current, deadZone);
            var lastValue = UnLerpShort(last, deadZone);
            var delta = Math.Clamp(-1, 1, currentValue - lastValue);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, currentValue, delta, elapsed));

            static double UnLerpShort(short value, double deadZone) => 0.5 + value switch
            {
                > 0 => 0.5 * MathUtils.UnLerp(MathUtils.Clamp01(deadZone) * short.MaxValue, short.MaxValue, value),
                < 0 => -0.5 * MathUtils.UnLerp(MathUtils.Clamp01(deadZone) * short.MinValue, short.MinValue, value),
                _ => 0
            };
        }

        void CreateAxisGestureByte(byte last, byte current, double deadZone, GamepadAxis axis)
        {
            if (current == last)
                return;
            if (current < deadZone && last < deadZone)
                return;

            var byteDeadZone = MathUtils.Clamp01(deadZone) * byte.MaxValue;
            var currentValue = MathUtils.UnLerp(byteDeadZone, byte.MaxValue, current);
            var lastValue = MathUtils.UnLerp(byteDeadZone, byte.MaxValue, last);
            var delta = Math.Clamp(-1, 1, currentValue - lastValue);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, currentValue, delta, elapsed));
        }

        CreateAxisGestureShort(last.RightThumbX, current.RightThumbX, _settings.RightThumbDeadZone, GamepadAxis.RightThumbX);
        CreateAxisGestureShort(last.RightThumbY, current.RightThumbY, _settings.RightThumbDeadZone, GamepadAxis.RightThumbY);

        CreateAxisGestureShort(last.LeftThumbX, current.LeftThumbX, _settings.LeftThumbDeadZone, GamepadAxis.LeftThumbX);
        CreateAxisGestureShort(last.LeftThumbY, current.LeftThumbY, _settings.LeftThumbDeadZone, GamepadAxis.LeftThumbY);

        CreateAxisGestureByte(last.RightTrigger, current.RightTrigger, _settings.TriggerDeadZone, GamepadAxis.RightTrigger);
        CreateAxisGestureByte(last.LeftTrigger, current.LeftTrigger, _settings.TriggerDeadZone, GamepadAxis.LeftTrigger);
    }

    private void HandleGesture(IInputGesture gesture)
        => OnGesture?.Invoke(this, gesture);

    private void Dispose(bool disposing)
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
