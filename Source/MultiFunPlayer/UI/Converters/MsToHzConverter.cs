using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class MsToHzConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int ms ? (int)Math.Round(1000d / ms) : 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int hz ? (int)Math.Round(1000d / hz) : 0;
}
