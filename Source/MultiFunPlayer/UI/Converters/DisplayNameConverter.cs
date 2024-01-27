using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class DisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        if (value is string)
            return null;
        if (value is Type type)
            return type.GetCustomAttribute<DisplayNameAttribute>().DisplayName;

        var valueType = value.GetType();
        if (valueType.IsEnum)
            return valueType.GetField(value.ToString()).GetCustomAttribute<DisplayNameAttribute>().DisplayName;

        return valueType.GetCustomAttribute<DisplayNameAttribute>().DisplayName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}