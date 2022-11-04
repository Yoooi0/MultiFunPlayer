using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Globalization;

namespace MultiFunPlayer.UI.Converters;

internal class ItemIndexInContainerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DependencyObject item)
            return false;

        var itemsControl = ItemsControl.ItemsControlFromItemContainer(item);
        return itemsControl.ItemContainerGenerator.IndexFromContainer(item);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}