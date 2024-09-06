using MultiFunPlayer.Common;
using PropertyChanged;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for SmartLimitPreview.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
internal sealed partial class SmartLimitPreview : UserControl
{
    [DoNotNotify]
    public ObservableConcurrentCollection<Point> Points
    {
        get => (ObservableConcurrentCollection<Point>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ObservableConcurrentCollection<Point>),
            typeof(SmartLimitPreview), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnPointsPropertyChanged)));

    [DoNotNotify]
    public double Input
    {
        get => (double)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public static readonly DependencyProperty InputProperty =
        DependencyProperty.Register(nameof(Input), typeof(double),
            typeof(SmartLimitPreview), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnInputPropertyChanged)));

    [DoNotNotify]
    public double Output
    {
        get => (double)GetValue(OutputProperty);
        private set => SetValue(OutputProperty, value);
    }

    public static readonly DependencyProperty OutputProperty =
        DependencyProperty.Register(nameof(Output), typeof(double),
            typeof(SmartLimitPreview), new FrameworkPropertyMetadata(double.NaN));

    [SuppressPropertyChangedWarnings]
    private static void OnPointsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SmartLimitPreview @this)
            return;

        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnInputPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SmartLimitPreview @this)
            return;

        @this.UpdateOutput();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    public SmartLimitPreview()
    {
        InitializeComponent();
    }

    private void UpdateOutput()
    {
        if (Points == null || Points.Count == 0)
            return;
        if (!double.IsFinite(Input))
            return;

        var x = Math.Clamp(Input, 0, 100);
        Output = Interpolation.Linear(Points, x);
    }
}