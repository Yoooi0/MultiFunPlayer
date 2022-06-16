using MahApps.Metro.Controls;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Behaviours;

public static class ListBoxAssist
{
    private static readonly ConcurrentDictionary<ListBox, bool> _locks = new();

    public static readonly DependencyProperty CanHorizontalScrollProperty =
        DependencyProperty.RegisterAttached("CanHorizontalScroll",
            typeof(bool), typeof(ListBoxAssist),
                new PropertyMetadata(false, OnCanHorizontalScrollPropertyChanged));

    public static bool GetCanHorizontalScroll(DependencyObject dp)
        => (bool)dp.GetValue(CanHorizontalScrollProperty);

    public static void SetCanHorizontalScroll(DependencyObject dp, bool value)
        => dp.SetValue(CanHorizontalScrollProperty, value);

    private static void OnCanHorizontalScrollPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not ListBox listBox)
            return;

        listBox.PreviewMouseWheel -= HandlePreviewMouseWheel;
        listBox.PreviewMouseWheel += HandlePreviewMouseWheel;
    }

    public static readonly DependencyProperty SelectedItemsProperty =
     DependencyProperty.RegisterAttached("SelectedItems",
         typeof(IList), typeof(ListBoxAssist),
             new PropertyMetadata(null, OnSelectedItemsPropertyChanged));

    public static IList GetSelectedItems(DependencyObject dp)
        => (IList)dp.GetValue(SelectedItemsProperty);

    public static void SetSelectedItems(DependencyObject dp, IList value)
        => dp.SetValue(SelectedItemsProperty, value);

    private static void OnSelectedItemsPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not ListBox listBox)
            return;

        _ = _locks.GetOrAdd(listBox, false);

        listBox.SelectionChanged -= HandleSelectionChanged;
        listBox.SelectionChanged += HandleSelectionChanged;

        if (e.OldValue is INotifyCollectionChanged oldCollectionChanged)
            oldCollectionChanged.CollectionChanged -= OnCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newCollectionChanged)
        {
            newCollectionChanged.CollectionChanged += OnCollectionChanged;
            OnCollectionChanged(newCollectionChanged, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_locks.TryUpdate(listBox, true, false))
                return;

            var selectedItems = listBox.SelectedItems;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                if (e.OldItems != null)
                    foreach (var item in e.OldItems)
                        selectedItems.Remove(item);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                if (e.NewItems != null)
                    foreach (var item in e.NewItems)
                        selectedItems.Add(item);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                selectedItems.Clear();

                foreach (var item in sender as IEnumerable)
                    selectedItems.Add(item);
            }

            _ = _locks.TryUpdate(listBox, false, true);
        }
    }

    private static void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        if (!_locks.TryUpdate(listBox, true, false))
            return;

        var selectedItems = GetSelectedItems(listBox);
        if (e.RemovedItems != null)
            foreach (var item in e.RemovedItems)
                selectedItems.Remove(item);

        if (e.AddedItems != null)
            foreach (var item in e.AddedItems)
                selectedItems.Add(item);

        _ = _locks.TryUpdate(listBox, false, true);
    }

    private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        var scrollviewer = listBox.FindChildren<ScrollViewer>(true).FirstOrDefault();
        if (e.Delta > 0)
            scrollviewer.LineLeft();
        else
            scrollviewer.LineRight();

        e.Handled = true;
    }
}
