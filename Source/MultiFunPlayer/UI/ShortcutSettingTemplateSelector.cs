using MultiFunPlayer.Shortcut;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI;

internal sealed class ShortcutSettingTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (container is not FrameworkElement element)
            return null;
        if (item is not IShortcutSetting setting)
            return null;

        if (setting.TemplateName != null)
            return element.FindResource(setting.TemplateName) as DataTemplate;

        var type = item.GetType().GetGenericArguments()[0];
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
            type = nullableType;

        var prefix = item switch
        {
            IOneOfShortcutSetting => "OneOf",
            _ => ""
        };

        var suffix = item switch
        {
            IShortcutSetting when type.IsAssignableTo(typeof(IEnumerable)) && type != typeof(string) => "List",
            _ => ""
        };

        var resource = element.TryFindResource($"{prefix}{type.Name}{suffix}Template") ?? element.FindResource($"{prefix}Default{suffix}Template");
        return resource as DataTemplate;
    }
}
