using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Threshold")]
internal sealed class ThresholdShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
{
    public double Threshold { get; set; } = 0.5;
    public ThresholdTriggerMode TriggerMode { get; set; } = ThresholdTriggerMode.Rising;

    protected override void Update(IAxisInputGesture gesture)
    {
        var isRising = gesture.Delta > 0 && gesture.Value >= Threshold && gesture.Value - gesture.Delta < Threshold;
        var isFalling = gesture.Delta < 0 && gesture.Value <= Threshold && gesture.Value - gesture.Delta > Threshold;
        var didTrigger = (isRising, isFalling, TriggerMode) switch
        {
            (true, false, ThresholdTriggerMode.Rising) => true,
            (false, true, ThresholdTriggerMode.Falling) => true,
            (true, true, _) => throw new UnreachableException(),
            (_, _, ThresholdTriggerMode.Both) => true,
            _ => false,
        };

        if (!didTrigger)
            return;

        Invoke(SimpleInputGestureData.Default);
    }

    protected override void PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        PrintProperty(builder, () => Threshold);
        PrintProperty(builder, () => TriggerMode);
    }
}

public enum ThresholdTriggerMode
{
    Rising,
    Falling,
    Both
}