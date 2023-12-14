using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class StringListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IList<string> list)
            throw new NotSupportedException();

        var separator = parameter as string ?? ", ";
        return string.Join(separator, list);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var result = Activator.CreateInstance(targetType);
        if (result is not IList<string> list)
            throw new NotSupportedException();

        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return result;

        var separator = parameter as string ?? ",";
        foreach (var item in s.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            list.Add(item);

        return result;
    }
}
