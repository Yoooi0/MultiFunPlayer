using System.Globalization;
using System.Windows.Data;
using Vortice.XInput;

namespace MultiFunPlayer.UI.Converters;

internal sealed class GamepadKeyToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not GamepadVirtualKey key)
            return null;

        return key switch
        {
            GamepadVirtualKey.RightShoulder => "Right Bumper",
            GamepadVirtualKey.LeftShoulder => "Left Bumper",
            GamepadVirtualKey.LeftTrigger => "Left Trigger",
            GamepadVirtualKey.RightTrigger => "Right Trigger",
            GamepadVirtualKey.DirectionalPadUp    => "D-Pad Up",
            GamepadVirtualKey.DirectionalPadDown  => "D-Pad Down",
            GamepadVirtualKey.DirectionalPadLeft  => "D-Pad Left",
            GamepadVirtualKey.DirectionalPadRight => "D-Pad Right",
            GamepadVirtualKey.LeftThumbPress => "Left Thumb",
            GamepadVirtualKey.RightThumbPress => "Right Thumb",
            GamepadVirtualKey.LeftThumbUp => "Left Thumb Up",
            GamepadVirtualKey.LeftThumbDown => "Left Thumb Down",
            GamepadVirtualKey.LeftThumbRight => "Left Thumb Right",
            GamepadVirtualKey.LeftThumbLeft  => "Left Thumb Left",
            GamepadVirtualKey.LeftThumbUpLeft    => "Left Thumb Up-Left",
            GamepadVirtualKey.LeftThumbUpRight   => "Left Thumb Up-Right",
            GamepadVirtualKey.LeftThumbDownRight => "Left Thumb Down-Right",
            GamepadVirtualKey.LeftThumbDownLeft  => "Left Thumb Down-Left",
            GamepadVirtualKey.RightThumbUp => "Right Thumb Up",
            GamepadVirtualKey.RightThumbDown => "Right Thumb Down",
            GamepadVirtualKey.RightThumbRight => "Right Thumb Right",
            GamepadVirtualKey.RightThumbLeft => "Right Thumb Left",
            GamepadVirtualKey.RightThumbUpLeft => "Right Thumb Up-Left",
            GamepadVirtualKey.RightThumbUpRight => "Right Thumb Up-Right",
            GamepadVirtualKey.RightThumbDownRight => "Right Thumb Down-Right",
            GamepadVirtualKey.RightThumbDownLeft => "Right Thumb Down-Left",
            GamepadVirtualKey.None => throw new NotImplementedException(),
            _ => key.ToString()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
