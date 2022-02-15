using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.UI;

public class ComboBoxTemplateSelector : DataTemplateSelector
{
    public DataTemplate SelectedItemTemplate { get; set; }
    public DataTemplateSelector SelectedItemTemplateSelector { get; set; }
    public DataTemplate DropdownItemsTemplate { get; set; }
    public DataTemplateSelector DropdownItemsTemplateSelector { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        var parent = container;
        while (parent is not null and not ComboBox and not ComboBoxItem)
            parent = VisualTreeHelper.GetParent(parent);

        return parent is ComboBoxItem ? DropdownItemsTemplate ?? DropdownItemsTemplateSelector?.SelectTemplate(item, container)
                                      : SelectedItemTemplate ?? SelectedItemTemplateSelector?.SelectTemplate(item, container);
    }
}
