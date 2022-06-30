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
public partial class SmartLimitPreview : UserControl, INotifyPropertyChanged
{
    public PointCollection LinePoints { get; set; }

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
            typeof(SmartLimitPreview), new FrameworkPropertyMetadata(50d,
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

        @this.RefreshLine();
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
    }

    private void RefreshLine()
    {
        if (Points == null || Points.Count == 0 || Canvas.ActualWidth == 0 || Canvas.ActualHeight == 0)
            return;

        var newLinePoints = new List<Point>(Points.Count + 2);
        newLinePoints.Add(new Point(0, Points[0].Y));
        newLinePoints.AddRange(Points);
        newLinePoints.Add(new Point(100, Points[^1].Y));

        LinePoints = new PointCollection(newLinePoints.Select(p => Canvas.ToCanvas(p)));
    }

    private void RefreshScrubber()
    {
        if (LinePoints == null || LinePoints.Count == 0)
            return;

        var x = MathUtils.Clamp(Input, 0, 100);
        var y = Interpolation.Linear(LinePoints, p => p.X, p => p.Y, x);

        Output = y;
        (Scrubber.Data as EllipseGeometry).Center = new Point(x, y);
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLine();
        RefreshScrubber();
    }

    [SuppressPropertyChangedWarnings]
    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshLine();
        RefreshScrubber();
    }

    public event PropertyChangedEventHandler PropertyChanged;
}