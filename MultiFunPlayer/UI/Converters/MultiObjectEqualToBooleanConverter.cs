using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class MultiObjectEqualToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        for(var i = 1; i < values.Length; i++)
        {
            if (values[i].GetType() != values[0].GetType())
                return false;

            if (!Equals(values[0], values[1]))
                return false;
        }

        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
