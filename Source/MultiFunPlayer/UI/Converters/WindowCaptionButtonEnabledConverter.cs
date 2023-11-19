using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class WindowCaptionButtonEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2 || values[0] is not Button button || values[1] is not ResizeMode resizeMode)
            return true;

        return button.Name switch
        {
            "PART_CloseButton" => true,
            "PART_MinimizeButton" => resizeMode != ResizeMode.NoResize,
            "PART_MaximizeRestoreButton" => (object)(resizeMode != ResizeMode.NoResize && resizeMode != ResizeMode.CanMinimize),
            _ => throw new UnreachableException(),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}