using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls;

[TemplatePart(Name = "PART_ItemsHolder", Type = typeof(Panel))]
public class NonReloadingTabControl : TabControl
{
    private Panel _itemsHolderPanel;

    public static readonly DependencyProperty AdditionalPanelContentProperty = 
        DependencyProperty.Register("AdditionalPanelContent", 
            typeof(DataTemplate), typeof(NonReloadingTabControl), 
                new FrameworkPropertyMetadata(null));

    public DataTemplate AdditionalPanelContent
    {
        get => (DataTemplate)GetValue(AdditionalPanelContentProperty);
        set => SetValue(AdditionalPanelContentProperty, value);
    }

    public NonReloadingTabControl()
    {
        Loaded += (_, _) =>
        {
            foreach (var item in Items)
                _ = CreateChildContentPresenter(item);

            var selectedCp = FindChildContentPresenter(SelectedItem);
            if (selectedCp != null)
                selectedCp.Loaded += (_, _) => UpdateSelectedItem();
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _itemsHolderPanel = GetTemplateChild("PART_ItemsHolder") as Panel;
        UpdateSelectedItem();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);

        if (_itemsHolderPanel == null)
            return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                _itemsHolderPanel.Children.Clear();
                break;

            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        var cp = FindChildContentPresenter(item);
                        if (cp != null)
                            _itemsHolderPanel.Children.Remove(cp);
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
        if (IsLoaded)
            UpdateSelectedItem();
    }

    private void UpdateSelectedItem()
    {
        if (_itemsHolderPanel == null)
            return;

        var item = GetSelectedTabItem();
        if (item != null)
            CreateChildContentPresenter(item);

        foreach (ContentPresenter child in _itemsHolderPanel.Children)
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

        _itemsHolderPanel.Children.Add(cp);
        return cp;
    }

    private ContentPresenter FindChildContentPresenter(object data)
    {
        if (data is TabItem)
            data = (data as TabItem).Content;

        if (data == null)
            return null;

        if (_itemsHolderPanel == null)
            return null;

        foreach (var cp in _itemsHolderPanel.Children.Cast<ContentPresenter>())
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
