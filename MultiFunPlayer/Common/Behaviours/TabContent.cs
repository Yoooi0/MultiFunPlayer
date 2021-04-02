// TabContent.cs, version 1.2
// The code in this file is Copyright (c) Ivan Krivyakov
// See http://www.ikriv.com/legal.php for more information

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace MultiFunPlayer.Common.Behaviours
{
    public static class TabContent
    {
        public static readonly DependencyProperty IsCachedProperty =
            DependencyProperty.RegisterAttached("IsCached",
                typeof(bool), typeof(TabContent),
                    new UIPropertyMetadata(false, OnIsCachedChanged));
        public static bool GetIsCached(DependencyObject obj) => (bool)obj.GetValue(IsCachedProperty);
        public static void SetIsCached(DependencyObject obj, bool value) => obj.SetValue(IsCachedProperty, value);

        public static readonly DependencyProperty TemplateProperty =
            DependencyProperty.RegisterAttached("Template",
                typeof(DataTemplate), typeof(TabContent),
                    new UIPropertyMetadata(null));
        public static DataTemplate GetTemplate(DependencyObject obj) => (DataTemplate)obj.GetValue(TemplateProperty);
        public static void SetTemplate(DependencyObject obj, DataTemplate value) => obj.SetValue(TemplateProperty, value);

        public static readonly DependencyProperty TemplateSelectorProperty =
            DependencyProperty.RegisterAttached("TemplateSelector",
                typeof(DataTemplateSelector), typeof(TabContent),
                    new UIPropertyMetadata(null));
        public static DataTemplateSelector GetTemplateSelector(DependencyObject obj) => (DataTemplateSelector)obj.GetValue(TemplateSelectorProperty);
        public static void SetTemplateSelector(DependencyObject obj, DataTemplateSelector value) => obj.SetValue(TemplateSelectorProperty, value);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly DependencyProperty InternalTabControlProperty =
            DependencyProperty.RegisterAttached("InternalTabControl",
                typeof(TabControl), typeof(TabContent),
                    new UIPropertyMetadata(null, OnInternalTabControlChanged));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static TabControl GetInternalTabControl(DependencyObject obj) => (TabControl)obj.GetValue(InternalTabControlProperty);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void SetInternalTabControl(DependencyObject obj, TabControl value) => obj.SetValue(InternalTabControlProperty, value);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly DependencyProperty InternalCachedContentProperty =
            DependencyProperty.RegisterAttached("InternalCachedContent",
                typeof(ContentControl), typeof(TabContent),
                    new UIPropertyMetadata(null));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static ContentControl GetInternalCachedContent(DependencyObject obj) => (ContentControl)obj.GetValue(InternalCachedContentProperty);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void SetInternalCachedContent(DependencyObject obj, ContentControl value) => obj.SetValue(InternalCachedContentProperty, value);

        public static readonly DependencyProperty InternalContentManagerProperty =
            DependencyProperty.RegisterAttached("InternalContentManager",
                typeof(object), typeof(TabContent),
                    new UIPropertyMetadata(null));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static object GetInternalContentManager(DependencyObject obj) => obj.GetValue(InternalContentManagerProperty);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void SetInternalContentManager(DependencyObject obj, object value) => obj.SetValue(InternalContentManagerProperty, value);

        private static void OnIsCachedChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj == null)
                return;

            if (obj is not TabControl tabControl)
                throw new InvalidOperationException($"Cannot set TabContent.IsCached on object of type {args.NewValue.GetType().Name}. Only objects of type TabControl can have TabContent.IsCached property.");

            if (args.NewValue is not bool newValue)
                return;

            if (!newValue)
                return;

            EnsureContentTemplateIsNull(tabControl);
            tabControl.ContentTemplate = CreateContentTemplate();
            EnsureContentTemplateIsNotModified(tabControl);
        }

        private static DataTemplate CreateContentTemplate()
        {
            const string xaml = "<DataTemplate><Border b:TabContent.InternalTabControl=\"{Binding RelativeSource={RelativeSource AncestorType=TabControl}}\" /></DataTemplate>";

            var context = new ParserContext
            {
                XamlTypeMapper = new XamlTypeMapper(new string[0])
            };

            context.XamlTypeMapper.AddMappingProcessingInstruction("b", typeof(TabContent).Namespace, typeof(TabContent).Assembly.FullName);
            context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            context.XmlnsDictionary.Add("b", "b");

            return XamlReader.Parse(xaml, context) as DataTemplate;
        }

        private static void OnInternalTabControlChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (obj == null)
                return;

            if (obj is not Decorator container)
                throw new InvalidOperationException($"Cannot set TabContent.InternalTabControl on object of type {obj.GetType().Name}. Only controls that derive from Decorator, such as Border can have a TabContent.InternalTabControl.");

            if (args.NewValue == null)
                return;

            if (args.NewValue is not TabControl tabControl)
                throw new InvalidOperationException($"Value of TabContent.InternalTabControl cannot be of type {args.NewValue.GetType().Name}, it must be of type TabControl");

            var contentManager = GetContentManager(tabControl, container);
            contentManager.UpdateSelectedTab();
        }

        private static ContentManager GetContentManager(TabControl tabControl, Decorator container)
        {
            var contentManager = (ContentManager)GetInternalContentManager(tabControl);
            if (contentManager != null)
                contentManager.ReplaceContainer(container);
            else
            {
                contentManager = new ContentManager(tabControl, container);
                SetInternalContentManager(tabControl, contentManager);
            }

            return contentManager;
        }

        private static void EnsureContentTemplateIsNull(TabControl tabControl)
        {
            if (tabControl.ContentTemplate != null)
                throw new InvalidOperationException("TabControl.ContentTemplate value is not null. If TabContent.IsCached is True, use TabContent.Template instead of ContentTemplate");
        }

        private static void EnsureContentTemplateIsNotModified(TabControl tabControl)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(TabControl.ContentTemplateProperty, typeof(TabControl));
            descriptor.AddValueChanged(tabControl, (sender, args)
                => throw new InvalidOperationException("Cannot assign to TabControl.ContentTemplate when TabContent.IsCached is True. Use TabContent.Template instead"));
        }

        public class ContentManager
        {
            private readonly TabControl _tabControl;
            private Decorator _border;

            public ContentManager(TabControl tabControl, Decorator border)
            {
                _tabControl = tabControl;
                _border = border;
                _tabControl.SelectionChanged += (sender, args) => UpdateSelectedTab();
            }

            public void ReplaceContainer(Decorator newBorder)
            {
                if (ReferenceEquals(_border, newBorder))
                    return;

                _border.Child = null;
                _border = newBorder;
            }

            public void UpdateSelectedTab() => _border.Child = GetCurrentContent();

            private ContentControl GetCurrentContent()
            {
                var item = _tabControl.SelectedItem;
                if (item == null)
                    return null;

                var tabItem = _tabControl.ItemContainerGenerator.ContainerFromItem(item);
                if (tabItem == null)
                    return null;

                var cachedContent = GetInternalCachedContent(tabItem);
                if (cachedContent == null)
                {
                    cachedContent = new ContentControl
                    {
                        DataContext = item,
                        ContentTemplate = GetTemplate(_tabControl),
                        ContentTemplateSelector = GetTemplateSelector(_tabControl)
                    };

                    cachedContent.SetBinding(ContentControl.ContentProperty, new Binding());
                    SetInternalCachedContent(tabItem, cachedContent);
                }

                return cachedContent;
            }
        }
    }
}