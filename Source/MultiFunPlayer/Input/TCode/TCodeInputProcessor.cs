using NLog;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Input.TCode;

internal class TCodeInputProcessor : IInputProcessor
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public event EventHandler<IInputGesture> OnGesture;
    public void Parse(string input)
    {
        Logger.Debug("Input: {1}", input);

        var buttonMatch = Regex.Match(input, "#(.+?):(0|1)");
        if (buttonMatch.Success)
        {
            var button = buttonMatch.Groups[1].Value;
            var state = int.Parse(buttonMatch.Groups[2].Value) == 1;
            HandleGesture(TCodeButtonGesture.Create(button, state));
        }
    }

    private void HandleGesture(IInputGesture gesture)
        => OnGesture?.Invoke(this, gesture);

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
