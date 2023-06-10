using PropertyChanged;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for DraggablePoint.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
public partial class DraggablePoint : UserControl
{
    public Point Position
    {
        get => (Point)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(Point),
            typeof(DraggablePoint), new FrameworkPropertyMetadata(default(Point),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null));

    public DraggablePoint()
    {
        InitializeComponent();
    }
}
