using MultiFunPlayer.Common;
using NLog;
using Vortice.XInput;

namespace MultiFunPlayer.Input.XInput;

public class XInputProcessor : IInputProcessor
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly State[] _states;

    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    public event EventHandler<IInputGesture> OnGesture;

    public XInputProcessor()
    {
        _states = new State[4];
        _cancellationSource = new CancellationTokenSource();
        _thread = new Thread(() => Update(_cancellationSource.Token))
        {
            IsBackground = true
        };

        _thread.Start();
    }

    private void Update(CancellationToken token)
    {
        const int sleepMs = (int)(1000 / 30f);

        Vortice.XInput.XInput.SetReporting(true);
        while (!token.IsCancellationRequested)
        {
            for (var i = 0; i < _states.Length; i++)
            {
                if (!Vortice.XInput.XInput.GetState(i, out var state))
                    continue;

                Logger.Trace("{0}, PacketNumber: {1}", state.Gamepad, state.PacketNumber);
                if (Vortice.XInput.XInput.GetKeystroke(i, out var keystroke))
                    ParseKeystrokeGestures(i, keystroke);

                var lastState = _states[i];
                if (lastState.PacketNumber == state.PacketNumber)
                    continue;

                ParseStateGestures(i, ref lastState.Gamepad, ref state.Gamepad);
                _states[i] = state;
            }

            Thread.Sleep(sleepMs);
        }
    }

    private void ParseKeystrokeGestures(int userIndex, Keystroke keystroke)
    {
        if (keystroke.Flags == KeyStrokeFlags.KeyDown)
            return;

        foreach (var button in EnumUtils.GetValues<GamepadVirtualKey>())
        {
            if ((keystroke.VirtualKey & button) != 0)
                HandleGesture(GamepadButtonGesture.Create(userIndex, button));
        }
    }

    private void ParseStateGestures(int userIndex, ref Gamepad last, ref Gamepad current)
    {
        void CreateAxisGestureShort(short last, short current, GamepadAxis axis)
        {
            var delta = (float)(current - last) / ushort.MaxValue;
            var value = MathUtils.Map(current, short.MinValue, short.MaxValue, 0, 1);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, value, delta));
        }

        void CreateAxisGestureByte(byte last, byte current, GamepadAxis axis)
        {
            var delta = (float)(current - last) / byte.MaxValue;
            var value = MathUtils.Map(current, byte.MinValue, byte.MaxValue, 0, 1);
            HandleGesture(GamepadAxisGesture.Create(userIndex, axis, value, delta));
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
