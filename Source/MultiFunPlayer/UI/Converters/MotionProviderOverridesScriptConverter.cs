using MultiFunPlayer.Common;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class MotionProviderOverridesScriptConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 3
        || values[0] is not IScriptResource script
        || values[1] is not string selectedMotionProvider
        || values[2] is not double motionProviderBlend)
            return null;

        return script != null && selectedMotionProvider != null && motionProviderBlend > 50;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
