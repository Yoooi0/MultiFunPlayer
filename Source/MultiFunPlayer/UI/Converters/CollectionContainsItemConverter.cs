using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class CollectionContainsItemConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not IEnumerable enumerable || values[1] is not object value)
            return false;

        if (enumerable is IList list)
            return list.Contains(value);
        return enumerable.OfType<object>().Contains(value);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
