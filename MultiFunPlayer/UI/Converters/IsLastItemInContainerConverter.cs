using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Globalization;

namespace MultiFunPlayer.UI.Converters;

public class IsLastItemInContainerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DependencyObject item)
            return false;

        var itemsControl = ListBox.ItemsControlFromItemContainer(item);
        var index = itemsControl.ItemContainerGenerator.IndexFromContainer(item);
        return index == itemsControl.Items.Count - 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}