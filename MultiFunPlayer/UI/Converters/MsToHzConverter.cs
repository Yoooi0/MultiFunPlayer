using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class MsToHzConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int ms ? (int)MathF.Round(1000f / ms) : 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int hz ? (int)MathF.Round(1000f / hz) : 0;
}
