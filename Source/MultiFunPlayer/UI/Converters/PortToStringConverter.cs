using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public sealed class PortToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not int port ? null : port.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s)
            return 0;

        var match = Regex.Match(s, @".*?(\d+).*");
        if (!match.Success)
            return 0;

        if (match.Groups.Count == 2 && int.TryParse(match.Groups[1].Value, out var port))
            return Math.Clamp(port, 0, 65535);

        return 0;
    }
}
