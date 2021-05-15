using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace MultiFunPlayer.Common.Converters
{
    public class CallObjectMethodWithArgumentConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 3)
                return null;

            var dataContext = values[0];
            var methodName = values[1] as string;
            var argument = values[2];

            var method = dataContext.GetType().GetMethod(methodName);
            return method.Invoke(dataContext, new[] { argument });
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
