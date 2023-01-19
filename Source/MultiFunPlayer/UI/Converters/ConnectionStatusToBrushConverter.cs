using MultiFunPlayer.Common;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Converters;

internal class ConnectionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (SolidColorBrush)Application.Current.Resources[value switch
        {
            ConnectionStatus.Connected => "MaterialDesignSuccessBrush",
            ConnectionStatus.Disconnected => "MaterialDesignErrorBrush",
            ConnectionStatus.Connecting or ConnectionStatus.Disconnecting => "MaterialDesignPendingBrush",
            _ => throw new UnreachableException()
        }];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal class ConnectionStatusToLightBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (SolidColorBrush)Application.Current.Resources[value switch
        {
            ConnectionStatus.Connected => "MaterialDesignLightSuccessBrush",
            ConnectionStatus.Disconnected => "MaterialDesignLightErrorBrush",
            ConnectionStatus.Connecting or ConnectionStatus.Disconnecting => "MaterialDesignLightPendingBrush",
            _ => throw new UnreachableException()
        }];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
