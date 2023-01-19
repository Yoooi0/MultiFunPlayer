using MaterialDesignThemes.Wpf.Converters;
using System.Windows;

namespace MultiFunPlayer.UI.Converters;

public class InvertedBooleanToVisibilityConverter : BooleanConverter<Visibility>
{
    public InvertedBooleanToVisibilityConverter()
        : base(Visibility.Collapsed, Visibility.Visible) { }
}