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
    public float Input
    {
        get => (float)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public static readonly DependencyProperty InputProperty =
        DependencyProperty.Register(nameof(Input), typeof(float),
            typeof(SmartLimitPreview), new FrameworkPropertyMetadata(50f,
                new PropertyChangedCallback(OnInputPropertyChanged)));

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
        if (Canvas.Points == null)
            return;

        if (Canvas.Points.Count > 0)
        {
            var newPoints = new PointCollection();
            newPoints.Add(new Point(0, Canvas.Points[0].Y));

            foreach (var point in Canvas.Points)
                newPoints.Add(point);

            newPoints.Add(new Point(100, Canvas.Points[^1].Y));

            LinePoints = newPoints;
        }
    }

    private void RefreshScrubber()
    {
        if (LinePoints == null || LinePoints.Count == 0)
            return;

        var x = MathUtils.Clamp(Input, 0, 100);
        var y = Interpolation.Linear(LinePoints, p => (float)p.X, p => (float)p.Y, x);

        (Scrubber.Data as EllipseGeometry).Center = new Point(x, y);
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLine();
        RefreshScrubber();
    }

    public event PropertyChangedEventHandler PropertyChanged;
}