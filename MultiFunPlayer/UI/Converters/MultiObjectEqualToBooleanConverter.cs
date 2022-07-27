using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class MultiObjectEqualToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        try
        {
            for (var i = 1; i < values.Length; i++)
            {
                if (values[i].GetType() == values[0].GetType() && !Equals(values[0], values[i]))
                    return false;

                if (values[i] is not IConvertible convertible)
                    return false;

                var converted = convertible.ToType(values[0].GetType(), CultureInfo.InvariantCulture);
                if (converted == null)
                    return false;

                if (converted.GetType() != values[0].GetType() || !Equals(values[0], converted))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
