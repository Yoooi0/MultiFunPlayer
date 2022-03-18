using Buttplug;
using MultiFunPlayer.OutputTarget.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class ButtplugClientDeviceConnectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 
         || values[0] is not ButtplugClientDeviceSettings settings 
         || values[1] is not IList<ButtplugClientDevice> availableDevices)
            return false;

        return availableDevices.Any(d => string.Equals(settings.DeviceName, d.Name, StringComparison.OrdinalIgnoreCase) && settings.DeviceIndex == d.Index);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
