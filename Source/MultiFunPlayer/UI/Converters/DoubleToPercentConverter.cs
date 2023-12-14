using MultiFunPlayer.Common;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public sealed class DoubleToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double x && double.IsFinite(x) ? Math.Clamp(Math.Round(x, 2) * 100, 0, 100) : 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double x && double.IsFinite(x) ? MathUtils.Clamp01(x / 100) : double.NaN;
}
