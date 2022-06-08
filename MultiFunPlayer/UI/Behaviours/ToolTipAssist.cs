using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        toolTip.Opened -= ToolTip_Opened;
        toolTip.Opened += ToolTip_Opened;
    }

    private static void ToolTip_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ToolTip toolTip)
            return;
    
        if (toolTip.PlacementTarget == null)
            return;
    
        void OnPlacementTargetMouseLeave(object sender, MouseEventArgs e)
        {
            toolTip.Visibility = Visibility.Hidden;
            toolTip.IsOpen = false;
        }
    
        toolTip.PlacementTarget.MouseLeave -= OnPlacementTargetMouseLeave;
        toolTip.PlacementTarget.MouseLeave += OnPlacementTargetMouseLeave;
    }
}
