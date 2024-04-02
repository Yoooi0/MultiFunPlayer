using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.TCode;
using MultiFunPlayer.Input.XInput;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Converters;

internal sealed class GestureDescriptorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string or null)
            return null;

        var colorHex = value switch
        {
            KeyboardGestureDescriptor => "#BD6ECA",
            MouseAxisGestureDescriptor => "#81C784",
            MouseButtonGestureDescriptor => "#81C784",
            TCodeAxisGestureDescriptor => "#D8494C",
            TCodeButtonGestureDescriptor => "#D8494C",
            GamepadAxisGestureDescriptor => "#F4B800",
            GamepadButtonGestureDescriptor => "#F4B800",
            _ => throw new NotImplementedException()
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

internal sealed class GestureDescriptorToPackIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            KeyboardGestureDescriptor => PackIconKind.KeyboardOutline,
            MouseAxisGestureDescriptor => PackIconKind.MouseMoveVertical,
            MouseButtonGestureDescriptor => PackIconKind.Mouse,
            TCodeAxisGestureDescriptor => PackIconKind.AlphaTBoxOutline,
            TCodeButtonGestureDescriptor => PackIconKind.AlphaTBoxOutline,
            GamepadAxisGestureDescriptor => PackIconKind.MicrosoftXboxController,
            GamepadButtonGestureDescriptor => PackIconKind.MicrosoftXboxController,
            null or string => PackIconKind.Abacus,
            _ => throw new NotImplementedException()
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
