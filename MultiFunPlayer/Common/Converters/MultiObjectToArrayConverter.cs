﻿using System;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.Common.Converters
{
    public class MultiObjectToArrayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Clone();
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
