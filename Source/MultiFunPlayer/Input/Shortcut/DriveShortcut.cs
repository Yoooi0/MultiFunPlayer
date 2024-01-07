using System.ComponentModel;

namespace MultiFunPlayer.Input.Binding;

[DisplayName("Axis Drive")]
public sealed class DriveShortcut(IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(gesture)
{
    public DriveShortcutBindingMode DriveMode { get; set; } = DriveShortcutBindingMode.Relative;
    public bool Invert { get; set; } = false;

    protected override IAxisInputGestureData CreateData(IAxisInputGesture gesture)
    {
        if (DriveMode == DriveShortcutBindingMode.Relative)
            return AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert);
        if (DriveMode == DriveShortcutBindingMode.Absolute)
            return AxisInputGestureData.FromGestureAbsolute(gesture, invertValue: Invert);

        if (DriveMode == DriveShortcutBindingMode.RelativeNegativeOnly)
            return gesture.Delta < 0 ? AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert) : null;
        if (DriveMode == DriveShortcutBindingMode.RelativePositiveOnly)
            return gesture.Delta > 0 ? AxisInputGestureData.FromGestureRelative(gesture, invertDelta: Invert) : null;

        if (DriveMode == DriveShortcutBindingMode.RelativeJoystick)
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
public enum DriveShortcutBindingMode
{
    Absolute,
    Relative,
    RelativePositiveOnly,
    RelativeNegativeOnly,
    RelativeJoystick
}