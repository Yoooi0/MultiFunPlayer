using Stylet.Xaml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Behaviours;

public static class ComboBoxAssist
{
    private static CommandAction _previewSelectionChangedHandler;

    public static readonly DependencyProperty PreviewSelectionChangedProperty =
        DependencyProperty.RegisterAttached("PreviewSelectionChanged",
            typeof(ICommand), typeof(ComboBoxAssist),
                new PropertyMetadata(null, OnPreviewSelectionChangedChanged));

    public static ICommand GetPreviewSelectionChanged(DependencyObject dp)
        => (ICommand)dp.GetValue(PreviewSelectionChangedProperty);

    public static void SetPreviewSelectionChanged(DependencyObject dp, ICommand value)
        => dp.SetValue(PreviewSelectionChangedProperty, value);

    private static void OnPreviewSelectionChangedChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not ComboBox comboBox || e.NewValue is not CommandAction handler)
            return;

        _previewSelectionChangedHandler = handler;
        comboBox.SelectionChanged -= OnSelectionChanged;
        comboBox.SelectionChanged += OnSelectionChanged;

        var binding = comboBox.GetBindingExpression(Selector.SelectedValueProperty);
        binding.UpdateTarget();
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        var binding = comboBox.GetBindingExpression(Selector.SelectedValueProperty);
        _previewSelectionChangedHandler?.Execute(e);
        binding.UpdateSource();
    }
}
