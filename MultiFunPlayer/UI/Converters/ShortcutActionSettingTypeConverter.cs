using MultiFunPlayer.Input;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class ShortcutActionSettingTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IShortcutSetting setting)
            return null;

        var type = setting.GetType().GetGenericArguments()[0];
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
            type = nullableType;

        return type;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
