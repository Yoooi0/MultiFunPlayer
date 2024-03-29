﻿using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Globalization;

namespace MultiFunPlayer.UI.Converters;

internal sealed class ItemIndexInContainerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DependencyObject item)
            return false;

        var offset = parameter is string s && int.TryParse(s, out var result) ? result : 0;
        var itemsControl = ItemsControl.ItemsControlFromItemContainer(item);
        return itemsControl.ItemContainerGenerator.IndexFromContainer(item) + offset;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}