using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Hold")]
internal sealed class ButtonHoldShortcut(IShortcutActionRunner actionRunner, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionRunner, gesture)
{
    public int MinimumHoldDuration { get; set; } = 1000;
    public int MaximumHoldDuration { get; set; } = -1;
    public ButtonHoldInvokeType InvokeType { get; set; } = ButtonHoldInvokeType.OnRelease;

    private int _pressTime;

    protected override void Update(ISimpleInputGesture gesture)
    {
        if (gesture.State && _pressTime == 0)
        {
            _pressTime = Environment.TickCount;

            if (InvokeType == ButtonHoldInvokeType.WhileHolding)
                Delay(MinimumHoldDuration, () => Invoke(SimpleInputGestureData.Default));
        }
        else if (!gesture.State && _pressTime > 0)
        {
            var duration = Environment.TickCount - _pressTime;
            _pressTime = 0;

            if (InvokeType == ButtonHoldInvokeType.WhileHolding)
            {
                CancelDelay();
            }
            else if (InvokeType == ButtonHoldInvokeType.OnRelease)
            {
                if (duration < MinimumHoldDuration)
                    return;
                if (MaximumHoldDuration > MinimumHoldDuration && duration > MaximumHoldDuration)
                    return;

                Invoke(SimpleInputGestureData.Default);
            }
        }
    }
}

internal enum ButtonHoldInvokeType
{
    OnRelease,
    WhileHolding
}