using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Behaviours;

public static class SliderAssist
{
    public static readonly DependencyProperty DefaultValueOnDoubleClickProperty =
        DependencyProperty.RegisterAttached("DefaultValueOnDoubleClick",
            typeof(float), typeof(SliderAssist),
                new PropertyMetadata(float.NaN, OnDefaultValueOnDoubleClickPropertyChanged));

    public static float GetDefaultValueOnDoubleClick(DependencyObject dp)
        => (float)dp.GetValue(DefaultValueOnDoubleClickProperty);

    public static void SetDefaultValueOnDoubleClick(DependencyObject dp, float value)
        => dp.SetValue(DefaultValueOnDoubleClickProperty, value);

    private static void OnDefaultValueOnDoubleClickPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not Slider slider)
            return;

        slider.MouseDoubleClick -= HandleMouseDoubleClick;
        slider.MouseDoubleClick += HandleMouseDoubleClick;
    }

    private static void HandleMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        slider.Value = GetDefaultValueOnDoubleClick(slider);
    }
}
