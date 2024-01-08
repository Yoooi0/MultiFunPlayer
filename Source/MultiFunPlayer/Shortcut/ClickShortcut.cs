using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Click")]
internal sealed class ClickShortcut(IShortcutActionResolver actionResolver, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
{
    private int _stateCounter;
    private int _lastClickTime;

    public int ClickCount { get; set; } = 2;
    public int MaximumClickInterval { get; set; } = 200;

    protected override void Update(ISimpleInputGesture gesture)
    {
        if (gesture.State && _stateCounter == 0)
        {
            _stateCounter++;
        }
        else if (gesture.State && _stateCounter > 0)
        {
            if (_stateCounter % 2 == 1) //consecutive press
                return;

            _stateCounter++;
            if (Environment.TickCount - _lastClickTime > MaximumClickInterval)
            {
                ResetState();
                return;
            }
        }
        else if (!gesture.State && _stateCounter > 0)
        {
            if (_stateCounter % 2 == 0) //consecutive release
                return;

            _stateCounter++;
            _lastClickTime = Environment.TickCount;
        }

        if (_stateCounter == 2 * ClickCount)
        {
            ResetState();
            Invoke(SimpleInputGestureData.FromGesture(gesture));
        }
    }

    private void ResetState()
    {
        _stateCounter = 0;
        _lastClickTime = 0;
    }
}