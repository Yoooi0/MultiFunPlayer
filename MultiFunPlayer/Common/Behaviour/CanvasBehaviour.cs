using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.Common.Behaviour
{
    public static class CanvasService
    {
        public static readonly DependencyProperty ChildrenProperty =
            DependencyProperty.RegisterAttached("Children", typeof(IEnumerable<UIElement>), typeof(CanvasService),
                new UIPropertyMetadata(OnChildrenChanged));

        private static readonly Dictionary<INotifyCollectionChanged, Canvas> _references = new Dictionary<INotifyCollectionChanged, Canvas>();

        public static IEnumerable<UIElement> GetChildren(Canvas canvas)
            => canvas.GetValue(ChildrenProperty) as IEnumerable<UIElement>;

        public static void SetChildren(Canvas canvas, IEnumerable<UIElement> children)
            => canvas.SetValue(ChildrenProperty, children);

        private static void OnChildrenChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not Canvas canvas)
                return;

            RepopulateChildren(canvas);
            var binding = canvas.GetBindingExpression(ChildrenProperty);
            if (binding == null)
            {
                _references.Clear();
                return;
            }

            var source = (binding.ResolvedSourcePropertyName == null
                ? binding.ResolvedSource
                : binding.ResolvedSource.GetType().GetProperty(binding.ResolvedSourcePropertyName).GetValue(binding.ResolvedSource));

            if (source is INotifyCollectionChanged reference)
            {
                var oldCanvas = _references.Keys.FirstOrDefault(c => c == canvas);
                if (oldCanvas != null)
                    _references.Remove(oldCanvas);

                _references[reference] = canvas;
                reference.CollectionChanged -= OnCollectionChanged;
                reference.CollectionChanged += OnCollectionChanged;
            }
        }

        private static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_references.TryGetValue(sender as INotifyCollectionChanged, out var canvas))
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        foreach (var item in e.NewItems)
                            canvas.Children.Add(item as UIElement);
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        foreach (var item in e.OldItems)
                            canvas.Children.Remove(item as UIElement);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        RepopulateChildren(canvas);
                        break;
                }
            }
        }

        private static void RepopulateChildren(Canvas canvas)
        {
            canvas.Children.Clear();
            foreach (var element in GetChildren(canvas))
                canvas.Children.Add(element);
        }
    }
}
