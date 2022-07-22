using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Behaviours;

public static class ToolTipAssist
{
    public static readonly DependencyProperty ForceCloseOnMouseLeaveProperty =
        DependencyProperty.RegisterAttached("ForceCloseOnMouseLeave",
            typeof(bool), typeof(ToolTipAssist),
                new PropertyMetadata(false, OnForceCloseOnMouseLeavePropertyChanged));

    public static bool GetForceCloseOnMouseLeave(DependencyObject dp)
        => (bool)dp.GetValue(ForceCloseOnMouseLeaveProperty);

    public static void SetForceCloseOnMouseLeave(DependencyObject dp, bool value)
        => dp.SetValue(ForceCloseOnMouseLeaveProperty, value);

    private static void OnForceCloseOnMouseLeavePropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not ToolTip toolTip)
            return;

        toolTip.Opened -= OnToolTipOpened;
        toolTip.Opened += OnToolTipOpened;
    }

    private static void OnToolTipOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ToolTip toolTip)
            return;

        if (toolTip.PlacementTarget == null)
            return;

        var placementTarget = toolTip.PlacementTarget;
        void OnPlacementTargetMouseLeave(object sender, MouseEventArgs e)
        {
            if (placementTarget != null)
                placementTarget.MouseLeave -= OnPlacementTargetMouseLeave;

            toolTip.Visibility = Visibility.Hidden;
            toolTip.IsOpen = false;
        }

        placementTarget.MouseLeave -= OnPlacementTargetMouseLeave;
        placementTarget.MouseLeave += OnPlacementTargetMouseLeave;
    }
}
