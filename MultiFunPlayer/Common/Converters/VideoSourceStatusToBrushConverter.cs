using MultiFunPlayer.VideoSource;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiFunPlayer.Common.Converters
{
    public class VideoSourceStatusToBrushConverter : IValueConverter
    {
        private IReadOnlyDictionary<VideoSourceStatus, Color> _colors = new Dictionary<VideoSourceStatus, Color>()
        {
            { VideoSourceStatus.Connected, Color.FromRgb(0x00, 0x80, 0x00) },
            { VideoSourceStatus.Disconnected, Color.FromRgb(0x00, 0x00, 0x00) },
            { VideoSourceStatus.Connecting, Color.FromRgb(0xb3, 0x9c, 0x09) },
            { VideoSourceStatus.Disconnecting, Color.FromRgb(0x0f, 0xa7, 0xa8) }
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is VideoSourceStatus status ? new SolidColorBrush(_colors[status]) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
