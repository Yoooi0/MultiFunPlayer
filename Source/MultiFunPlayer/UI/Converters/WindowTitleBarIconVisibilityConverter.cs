using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class WindowTitleBarIconVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2 || values[1] is not WindowStyle windowStyle)
            return Visibility.Visible;

        var icon = values[0];
        if (icon != null && (windowStyle == WindowStyle.SingleBorderWindow || windowStyle == WindowStyle.ThreeDBorderWindow))
            return Visibility.Visible;
        else
            return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}