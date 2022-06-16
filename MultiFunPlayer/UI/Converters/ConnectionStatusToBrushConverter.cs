using MultiFunPlayer.Common;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Converters;

public class ConnectionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ConnectionStatus.Connected => (SolidColorBrush)Application.Current.Resources["MaterialDesignSuccessBrush"],
            ConnectionStatus.Disconnected => (SolidColorBrush)Application.Current.Resources["MaterialDesignErrorBrush"],
            ConnectionStatus.Connecting or ConnectionStatus.Disconnecting => new SolidColorBrush(Color.FromRgb(0xb3, 0x9c, 0x09)),
            _ => null
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
