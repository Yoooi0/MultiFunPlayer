using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class WindowCaptionButtonVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 3 || values[0] is not Button button || values[1] is not WindowStyle windowStyle || values[2] is not ResizeMode resizeMode)
            return Visibility.Visible;

        if (button.Name == "PART_CloseButton")
            return (windowStyle != WindowStyle.None) ? Visibility.Visible : Visibility.Collapsed;
        else if (resizeMode != ResizeMode.NoResize && (windowStyle == WindowStyle.SingleBorderWindow || windowStyle == WindowStyle.ThreeDBorderWindow))
            return Visibility.Visible;
        else
            return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}