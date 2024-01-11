using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Drive")]
internal sealed class DriveShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(actionResolver, gesture)
{
    public DriveShortcutMode DriveMode { get; set; } = DriveShortcutMode.Relative;
    public bool Invert { get; set; } = false;

    protected override void Update(IAxisInputGesture gesture)
    {
        switch (DriveMode)
        {
            case DriveShortcutMode.Relative:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case DriveShortcutMode.Absolute:
                Invoke(AxisInputGestureData.FromGestureAbsolute(gesture, invertValue: Invert));
                break;
            case DriveShortcutMode.RelativeNegativeOnly when gesture.Delta < 0:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case DriveShortcutMode.RelativePositiveOnly when gesture.Delta > 0:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case DriveShortcutMode.RelativeJoystick:
                if (gesture.Value > 0.5 && gesture.Delta < 0)
                    break;
                if (gesture.Value < 0.5 && gesture.Delta > 0)
                    break;
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
        }
    }

    protected override void PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        PrintProperty(builder, () => DriveMode);
        PrintProperty(builder, () => Invert);
    }
}
public enum DriveShortcutMode
{
    Absolute,
    Relative,
    RelativePositiveOnly,
    RelativeNegativeOnly,
    RelativeJoystick
}