using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;
internal class AutoToolTipValueToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = double.Parse((value as string).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture);
        return string.Format($"{{0:P{parameter ?? 0}}}", v);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
