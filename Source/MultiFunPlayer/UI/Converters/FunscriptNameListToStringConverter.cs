using MultiFunPlayer.Common;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class FunscriptNameListToStringConverter : IValueConverter
{
    private const StringSplitOptions _splitOptions = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IList<string> list)
            throw new NotSupportedException();

        var items = list.NotNull()
                        .Select(x => x.Split('.', _splitOptions).LastOrDefault())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", items);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var result = Activator.CreateInstance(targetType);
        if (result is not IList<string> list)
            throw new NotSupportedException();

        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return result;

        var items = s.Split(',', _splitOptions)
                     .Select(x => x.Split('.', _splitOptions).LastOrDefault())
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
                list.Add(item);

        return result;
    }
}
