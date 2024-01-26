using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Drive")]
internal sealed class AxisDriveShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(actionResolver, gesture)
{
    public AxisDriveShortcutMode DriveMode { get; set; } = AxisDriveShortcutMode.Relative;
    public bool Invert { get; set; } = false;

    protected override void Update(IAxisInputGesture gesture)
    {
        switch (DriveMode)
        {
            case AxisDriveShortcutMode.Relative:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case AxisDriveShortcutMode.Absolute:
                Invoke(AxisInputGestureData.FromGestureAbsolute(gesture, invertValue: Invert));
                break;
            case AxisDriveShortcutMode.RelativeNegativeOnly when gesture.Delta < 0:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case AxisDriveShortcutMode.RelativePositiveOnly when gesture.Delta > 0:
                Invoke(AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert));
                break;
            case AxisDriveShortcutMode.RelativeJoystick:
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
internal enum AxisDriveShortcutMode
{
    Absolute,
    Relative,
    RelativePositiveOnly,
    RelativeNegativeOnly,
    RelativeJoystick
}