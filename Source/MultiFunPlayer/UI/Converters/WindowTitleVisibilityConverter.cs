using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class WindowTitleVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 1 || values[0] is not WindowStyle windowStyle)
            return Visibility.Visible;

        return windowStyle != WindowStyle.None ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}