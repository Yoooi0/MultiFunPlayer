using System;
using System.Globalization;
using System.Net;
using System.Windows.Data;

namespace MultiFunPlayer.Common.Converters
{
    public class IPEndPointToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is IPEndPoint endpoint ? endpoint.ToString() : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && IPEndPoint.TryParse(s, out var endpoint) ? endpoint : null;
    }
}
