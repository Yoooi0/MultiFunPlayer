using MaterialDesignThemes.Wpf;
using System.Windows;

namespace MultiFunPlayer.UI.Behaviours;

internal static class BadgedAssist
{
    public static readonly DependencyProperty AutoSizeToBadgeContentProperty =
        DependencyProperty.RegisterAttached("AutoSizeToBadgeContent",
            typeof(bool), typeof(BadgedAssist),
                new PropertyMetadata(false, OnAutoSizeToBadgeContentPropertyChanged));

    public static bool GetAutoSizeToBadgeContent(DependencyObject dp)
        => (bool)dp.GetValue(AutoSizeToBadgeContentProperty);

    public static void SetAutoSizeToBadgeContent(DependencyObject dp, bool value)
        => dp.SetValue(AutoSizeToBadgeContentProperty, value);

    private static void OnAutoSizeToBadgeContentPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not Badged badged)
            return;

        badged.SizeChanged += OnSizeChanged;
    }

    private static void OnSizeChanged(object sender, EventArgs e)
    {
        if (sender is not Badged badged)
            return;

        var container = badged.Template.FindName("PART_BadgeContainer", badged) as FrameworkElement;
        badged.SizeChanged -= OnSizeChanged;
        container.SizeChanged += (s, e) => badged.Width = container.ActualWidth;
    }
}
