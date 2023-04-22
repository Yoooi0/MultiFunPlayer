using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal class NullBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value == null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}