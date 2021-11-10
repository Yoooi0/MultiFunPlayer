using MultiFunPlayer.Common;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class DeviceAxisFriendlyNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DeviceAxis axis ? axis.FriendlyName : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string friendlyName ? DeviceAxis.All.First(a => string.Equals(a.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase)) : default;
}
