using System.ComponentModel;

namespace MultiFunPlayer.Input.Shortcut;

[DisplayName("Button Long Press")]
public sealed class LongPressShortcut(ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(gesture)
{
    public int MinimumHoldDuration { get; set; } = 1000;
    public int MaximumHoldDuration { get; set; } = -1;

    private int _pressTime;

    protected override ISimpleInputGestureData CreateData(ISimpleInputGesture gesture)
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
                return null;
            if (MaximumHoldDuration > MinimumHoldDuration && duration > MaximumHoldDuration)
                return null;

            return SimpleInputGestureData.FromGesture(gesture);
        }

        return null;
    }
}