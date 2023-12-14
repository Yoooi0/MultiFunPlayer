using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public sealed class UriToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Uri uri ? uri.ToString() : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && Uri.TryCreate(s, UriKind.Absolute, out var uri) ? uri : null;
}
