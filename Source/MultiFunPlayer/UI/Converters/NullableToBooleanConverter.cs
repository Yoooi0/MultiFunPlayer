using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class NullableToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value == null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
