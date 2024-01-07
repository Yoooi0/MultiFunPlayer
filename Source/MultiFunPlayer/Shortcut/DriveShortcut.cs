using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Drive")]
public sealed class DriveShortcut(IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(gesture)
{
    public DriveShortcutMode DriveMode { get; set; } = DriveShortcutMode.Relative;
    public bool Invert { get; set; } = false;

    protected override IAxisInputGestureData CreateData(IAxisInputGesture gesture)
    {
        if (DriveMode == DriveShortcutMode.Relative)
            return AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert);
        if (DriveMode == DriveShortcutMode.Absolute)
            return AxisInputGestureData.FromGestureAbsolute(gesture, invertValue: Invert);

        if (DriveMode == DriveShortcutMode.RelativeNegativeOnly)
            return gesture.Delta < 0 ? AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert) : null;
        if (DriveMode == DriveShortcutMode.RelativePositiveOnly)
            return gesture.Delta > 0 ? AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert) : null;

        if (DriveMode == DriveShortcutMode.RelativeJoystick)
        {
            if (gesture.Value > 0.5 && gesture.Delta < 0)
                return null;
            if (gesture.Value < 0.5 && gesture.Delta > 0)
                return null;
            return AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert);
        }

        return null;
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