using System.ComponentModel;

namespace MultiFunPlayer.Input.Binding;

[DisplayName("Button Click")]
public sealed class ClickShortcut(ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(gesture)
{
    private int _stateCounter;
    private int _lastClickTime;

    public int ClickCount { get; set; } = 2;
    public int MaximumClickInterval { get; set; } = 200;

    protected override ISimpleInputGestureData CreateData(ISimpleInputGesture gesture)
    {
        if (gesture.State && _stateCounter == 0)
        {
            _stateCounter++;
        }
        else if (gesture.State && _stateCounter > 0)
        {
            if (_stateCounter % 2 == 1) //consecutive press
                return null;

            _stateCounter++;
            if (Environment.TickCount - _lastClickTime > MaximumClickInterval)
            {
                ResetState();
                return null;
            }
        }
        else if (!gesture.State && _stateCounter > 0)
        {
            if (_stateCounter % 2 == 0) //consecutive release
                return null;

            _stateCounter++;
            _lastClickTime = Environment.TickCount;
        }

        if (_stateCounter == 2 * ClickCount)
        {
            ResetState();
            return SimpleInputGestureData.FromGesture(gesture);
        }

        return null;
    }

    private void ResetState()
    {
        _stateCounter = 0;
        _lastClickTime = 0;
    }
}