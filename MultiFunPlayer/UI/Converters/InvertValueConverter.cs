using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class InvertValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string s)
            throw new ArgumentException("Converter parameter must be provided");
        return Invert(value, s);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string s)
            throw new ArgumentException("Converter parameter must be provided");
        return Invert(value, s);
    }

    private object Invert(object value, string parameter)
    {
        if (value is int i) return int.Parse(parameter) - i;
        if (value is double d) return double.Parse(parameter) - d;
        if (value is float f) return float.Parse(parameter) - f;
        throw new NotSupportedException();
    }
}
