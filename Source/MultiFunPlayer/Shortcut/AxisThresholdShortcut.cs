using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Threshold")]
internal sealed class AxisThresholdShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
{
    public double Threshold { get; set; } = 0.5;
    public AxisThresholdTriggerMode TriggerMode { get; set; } = AxisThresholdTriggerMode.Rising;

    protected override void Update(IAxisInputGesture gesture)
    {
        var isRising = gesture.Delta > 0 && gesture.Value >= Threshold && gesture.Value - gesture.Delta < Threshold;
        var isFalling = gesture.Delta < 0 && gesture.Value <= Threshold && gesture.Value - gesture.Delta > Threshold;
        var didTrigger = (isRising, isFalling, TriggerMode) switch
        {
            (true, false, AxisThresholdTriggerMode.Rising) => true,
            (false, true, AxisThresholdTriggerMode.Falling) => true,
            (true, true, _) => throw new UnreachableException(),
            (_, _, AxisThresholdTriggerMode.Both) => true,
            _ => false,
        };

        if (!didTrigger)
            return;

        Invoke(SimpleInputGestureData.Default);
    }
}

internal enum AxisThresholdTriggerMode
{
    Rising,
    Falling,
    Both
}