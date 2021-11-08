using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MultiFunPlayer.UI.Controls
{
    /// <summary>
    /// Extended TabControl which saves the displayed item so you don't get the performance hit of
    /// unloading and reloading the VisualTree when switching tabs
    /// </summary>
    /// <remarks>
    /// Based on example from http://stackoverflow.com/a/9802346, which in turn is based on
    /// http://www.pluralsight-training.net/community/blogs/eburke/archive/2009/04/30/keeping-the-wpf-tab-control-from-destroying-its-children.aspx
    /// with some modifications so it reuses a TabItem's ContentPresenter when doing drag/drop operations
    /// </remarks>
    [TemplatePart(Name = "PART_ItemsHolder", Type = typeof(Panel))]
    public class NonReloadingTabControl : TabControl
    {
        private Panel itemsHolderPanel;

        public NonReloadingTabControl()
        {
            ItemContainerGenerator.StatusChanged += ItemContainerGeneratorStatusChanged;

            Loaded += (_, _) =>
            {
                foreach (var item in Items)
                    _ = CreateChildContentPresenter(item);

                var selectedCp = FindChildContentPresenter(SelectedItem);
                if (selectedCp != null)
                    selectedCp.Loaded += (_, _) => UpdateSelectedItem();
            };
        }
        private void ItemContainerGeneratorStatusChanged(object sender, EventArgs e)
        {
            if (ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                ItemContainerGenerator.StatusChanged -= ItemContainerGeneratorStatusChanged;

            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            itemsHolderPanel = GetTemplateChild("PART_ItemsHolder") as Panel;
            UpdateSelectedItem();
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);

            if (itemsHolderPanel == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    itemsHolderPanel.Children.Clear();
                    break;

                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            var cp = FindChildContentPresenter(item);
                            if (cp != null)
                                itemsHolderPanel.Children.Remove(cp);
                        }
                    }

                    UpdateSelectedItem();
                    break;

                case NotifyCollectionChangedAction.Replace:
                    throw new NotImplementedException("Replace not implemented yet");
            }
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if(IsLoaded)
                UpdateSelectedItem();
        }

        private void UpdateSelectedItem()
        {
            if (itemsHolderPanel == null)
                return;

            var item = GetSelectedTabItem();
            if (item != null)
                CreateChildContentPresenter(item);

            foreach (ContentPresenter child in itemsHolderPanel.Children)
                child.Visibility = (child.Tag as TabItem).IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private ContentPresenter CreateChildContentPresenter(object item)
        {
            if (item == null)
                return null;

            var cp = FindChildContentPresenter(item);
            if (cp != null)
                return cp;

            var tabItem = item as TabItem;
            cp = new ContentPresenter
            {
                Content = (tabItem != null) ? tabItem.Content : item,
                ContentTemplate = SelectedContentTemplate,
                ContentTemplateSelector = SelectedContentTemplateSelector,
                ContentStringFormat = SelectedContentStringFormat,
                Visibility = Visibility.Hidden,
                Tag = tabItem ?? ItemContainerGenerator.ContainerFromItem(item)
            };

            itemsHolderPanel.Children.Add(cp);
            return cp;
        }

        private ContentPresenter FindChildContentPresenter(object data)
        {
            if (data is TabItem)
                data = (data as TabItem).Content;

            if (data == null)
                return null;

            if (itemsHolderPanel == null)
                return null;

            foreach (var cp in itemsHolderPanel.Children.Cast<ContentPresenter>())
                if (cp.Content == data)
                    return cp;

            return null;
        }

        protected TabItem GetSelectedTabItem()
        {
            var selectedItem = SelectedItem;
            if (selectedItem == null)
                return null;

            return selectedItem as TabItem ?? ItemContainerGenerator.ContainerFromIndex(SelectedIndex) as TabItem;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new TabControlAutomationPeer(this);
    }
}
