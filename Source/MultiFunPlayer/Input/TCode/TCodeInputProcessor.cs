using MultiFunPlayer.Common;
using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Input.TCode;

internal sealed class TCodeInputProcessor : IInputProcessor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, bool> _buttonStates;
    private readonly Dictionary<string, int> _unsignedAxisStates;
    private readonly Dictionary<string, int> _signedAxisStates;

    public event EventHandler<IInputGesture> OnGesture;

    public TCodeInputProcessor()
    {
        _buttonStates = [];
        _unsignedAxisStates = [];
        _signedAxisStates = [];
    }

    public void Parse(string input)
    {
        Logger.Trace("Parsing {0}", input);
        foreach(var match in Regex.Matches(input, "#(?<button>.+?):(?<state>0|1)").OfType<Match>().Where(m => m.Success))
            CreateButtonGesture(match);

        foreach (var match in Regex.Matches(input, @"@(?<axis>.+?):(?<value>\d{1,5})").OfType<Match>().Where(m => m.Success))
            CreateAxisGesture(_unsignedAxisStates, match, ushort.MinValue, ushort.MaxValue);

        foreach (var match in Regex.Matches(input, @"\$(?<axis>.+?):(?<value>-?\d{1,5})").OfType<Match>().Where(m => m.Success))
            CreateAxisGesture(_signedAxisStates, match, short.MinValue, short.MaxValue);

        void CreateButtonGesture(Match match)
        {
            var button = match.Groups["button"].Value;
            var state = int.Parse(match.Groups["state"].Value) == 1;
            if (!state && _buttonStates.TryGetValue(button, out var lastState) && lastState)
                HandleGesture(TCodeButtonGesture.Create(button));

            _buttonStates[button] = state;
        }

        void CreateAxisGesture(Dictionary<string, int> states, Match match, double minValue, double maxValue)
        {
            var axis = match.Groups["axis"].Value;
            var value = int.Parse(match.Groups["value"].Value);
            states.TryAdd(axis, 0);

            var delta = value - states[axis];
            if (delta == 0)
                return;

            var valueDecimal = MathUtils.UnLerp(minValue, maxValue, value);
            var deltaDecimal = MathUtils.UnLerp(minValue, maxValue, delta);

            HandleGesture(TCodeAxisGesture.Create(axis, valueDecimal, deltaDecimal, 0));
            states[axis] = value;
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
