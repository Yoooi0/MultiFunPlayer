using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.Common.Controls
{
    public class GenericArgumentTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (container is not FrameworkElement element)
                return null;
            if (element == null || item == null)
                return null;

            var type = item.GetType().GetGenericArguments()[0];
            return element.FindResource($"{type.Name}Template") as DataTemplate;
        }
    }
}
