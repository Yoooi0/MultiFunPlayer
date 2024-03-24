using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Long Press")]
internal sealed class ButtonLongPressShortcut(IShortcutActionRunner actionRunner, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionRunner, gesture)
{
    public int MinimumHoldDuration { get; set; } = 1000;
    public int MaximumHoldDuration { get; set; } = -1;

    private int _pressTime;

    protected override void Update(ISimpleInputGesture gesture)
    {
        if (gesture.State && _pressTime == 0)
        {
            _pressTime = Environment.TickCount;
        }
        else if (!gesture.State && _pressTime > 0)
        {
            var duration = Environment.TickCount - _pressTime;

            _pressTime = 0;
            if (duration < MinimumHoldDuration)
                return;
            if (MaximumHoldDuration > MinimumHoldDuration && duration > MaximumHoldDuration)
                return;

            Invoke(SimpleInputGestureData.Default);
        }
    }
}