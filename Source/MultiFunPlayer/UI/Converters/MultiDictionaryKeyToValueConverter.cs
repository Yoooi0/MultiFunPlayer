using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal sealed class MultiDictionaryKeyToValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2)
            throw new NotSupportedException();

        if (!values[0].GetType().GetInterfaces().Any(i => i.GetGenericTypeDefinition().IsAssignableTo(typeof(IDictionary<,>))))
            throw new NotSupportedException();

        var method = values[0].GetType().GetMethod("get_Item");
        return method.Invoke(values[0], [values[1]]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
