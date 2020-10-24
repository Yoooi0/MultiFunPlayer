using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace MultiFunPlayer.Common.Converters
{
    class DeviceAxisFriendlyNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DeviceAxis axis ? axis.FriendlyName() : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string friendlyName ? EnumUtils.GetValues<DeviceAxis>().First(a => string.Compare(a.FriendlyName(), friendlyName, StringComparison.OrdinalIgnoreCase) == 0) : default;
    }
}
