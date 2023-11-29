using MultiFunPlayer.Common;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Input.TCode;

internal sealed class TCodeInputProcessor : IInputProcessor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, bool> _buttonStates;
    private readonly Dictionary<string, int> _axisStates;

    public event EventHandler<IInputGesture> OnGesture;

    public TCodeInputProcessor()
    {
        _buttonStates = [];
        _axisStates = [];
    }

    public void Parse(string input)
    {
        foreach(var match in Regex.Matches(input, "#(.+?):(0|1)").OfType<Match>())
        {
            if (!match.Success)
                continue;

            var button = match.Groups[1].Value;
            var state = int.Parse(match.Groups[2].Value) == 1;
            if (!state && _buttonStates.TryGetValue(button, out var lastState) && lastState)
                HandleGesture(TCodeButtonGesture.Create(button));

            _buttonStates[button] = state;
        }

        foreach(var match in Regex.Matches(input, @"@(.+?):(\d{1,5})").OfType<Match>())
        {
            if (!match.Success)
                continue;

            var axis = match.Groups[1].Value;
            var value = int.Parse(match.Groups[2].Value);
            _axisStates.TryAdd(axis, 0);

            var lastValue = _axisStates[axis];
            var delta = value - lastValue;
            if (delta == 0)
                continue;

            var valueDecimal = MathUtils.UnLerp(0, ushort.MaxValue, value);
            var deltaDecimal = MathUtils.UnLerp(0, ushort.MaxValue, delta);

            HandleGesture(TCodeAxisGesture.Create(axis, valueDecimal, deltaDecimal, 0));
            _axisStates[axis] = value;
        }
    }

    private void HandleGesture(IInputGesture gesture)
        => OnGesture?.Invoke(this, gesture);

    private void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
