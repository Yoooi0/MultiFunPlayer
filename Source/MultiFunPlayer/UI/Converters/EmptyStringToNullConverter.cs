using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class EmptyStringToNullConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Convert(value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Convert(value);

    private string Convert(object value)
    {
        if (value == null)
            return null;
        if (value is string s)
            return string.IsNullOrEmpty(s) ? null : s;
        throw new UnreachableException();
    }
}
