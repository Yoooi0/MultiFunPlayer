using MultiFunPlayer.Common;
using MultiFunPlayer.MotionProvider;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class MotionProviderSelectorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if(values.Length != 3
        || values[0] is not IMotionProviderManager manager
        || values[1] is not DeviceAxis axis
        || values[2] is not string motionProviderName)
            return null;

        return manager.GetMotionProvider(axis, motionProviderName);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
