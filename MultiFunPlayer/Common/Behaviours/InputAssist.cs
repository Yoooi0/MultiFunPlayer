using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Behaviours
{
    public static class InputAssist
    {
        public static readonly DependencyProperty UpdateSourceOnEnterProperty =
            DependencyProperty.RegisterAttached("UpdateSourceOnEnter",
                typeof(DependencyProperty), typeof(InputAssist),
                    new PropertyMetadata(null, OnUpdateSourceOnEnterPropertyChanged));

        public static void SetUpdateSourceOnEnter(DependencyObject dp, DependencyProperty value)
        {
            dp.SetValue(UpdateSourceOnEnterProperty, value);
        }

        public static DependencyProperty GetUpdateSourceOnEnter(DependencyObject dp)
            => (DependencyProperty)dp.GetValue(UpdateSourceOnEnterProperty);

        private static void OnUpdateSourceOnEnterPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not UIElement element)
                return;

            if (e.OldValue != null)
                element.PreviewKeyDown -= HandlePreviewKeyDown;

            if (e.NewValue != null)
                element.PreviewKeyDown += HandlePreviewKeyDown;
        }

        static void HandlePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                UpdateSource(e.Source);
        }

        static void UpdateSource(object source)
        {
            var property = GetUpdateSourceOnEnter(source as DependencyObject);
            if (property == null)
                return;

            if (source is not UIElement elt)
                return;

            var binding = BindingOperations.GetBindingExpression(elt, property);
            if (binding != null)
                binding.UpdateSource();
        }
    }
}
