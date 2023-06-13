using MultiFunPlayer.Common;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for SmartLimitPreview.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
internal partial class SmartLimitPreview : UserControl
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

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= @this.OnPointsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += @this.OnPointsCollectionChanged;

        @this.RefreshScrubber();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnInputPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SmartLimitPreview @this)
            return;

        @this.RefreshScrubber();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    public SmartLimitPreview()
    {
        InitializeComponent();

        IsVisibleChanged += (_, _) => RefreshScrubber();
    }

    private void RefreshScrubber()
    {
        if (!IsVisible)
            return;

        var canRefresh = CanRefresh();
        Scrubber.Visibility = canRefresh ? Visibility.Visible : Visibility.Collapsed;
        if (!canRefresh)
            return;

        var x = Math.Clamp(Input, 0, 100);
        var y = Interpolation.Linear(Points, p => p.X, p => p.Y, x);

        Output = y;
        (Scrubber.Data as EllipseGeometry).Center = Canvas.ToCanvas(new Point(x, y));

        bool CanRefresh()
        {
            if (Points == null || Points.Count == 0)
                return false;
            if (!double.IsFinite(Input))
                return false;
            return true;
        }
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshScrubber();

    [SuppressPropertyChangedWarnings]
    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RefreshScrubber();
}