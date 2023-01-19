using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal class ValueConverterGroup : List<IValueConverter>, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => this.Aggregate(value, (current, converter) => converter.Convert(current, targetType, parameter, culture));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
