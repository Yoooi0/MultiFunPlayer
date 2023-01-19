using MultiFunPlayer.Input;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI;

internal class ShortcutSettingTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (container is not FrameworkElement element)
            return null;
        if (element == null || item == null)
            return null;

        var type = item.GetType().GetGenericArguments()[0];
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
            type = nullableType;

        var prefix = item switch
        {
            IOneOfShortcutSetting _ => "OneOf",
            _ => ""
        };

        var resource = element.TryFindResource($"{prefix}{type.Name}Template") ?? element.FindResource($"{prefix}DefaultTemplate");
        return resource as DataTemplate;
    }
}
