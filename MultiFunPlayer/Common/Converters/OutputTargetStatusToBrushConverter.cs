using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.VideoSource;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiFunPlayer.Common.Converters
{
    public class OutputTargetStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                OutputTargetStatus.Connected => new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)),
                OutputTargetStatus.Disconnected => new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
                OutputTargetStatus.Connecting or OutputTargetStatus.Disconnecting => new SolidColorBrush(Color.FromRgb(0xb3, 0x9c, 0x09)),
                _ => null
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
