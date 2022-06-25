using MultiFunPlayer.Common;
using PropertyChanged;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for OpenSimplexPreview.xaml
/// </summary>
public partial class OpenSimplexPreview : UserControl, INotifyPropertyChanged
{
    private readonly OpenSimplex _noise;
    private double _seed;

    public PointCollection Points { get; set; }

    [DoNotNotify]
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public static readonly DependencyProperty LengthProperty =
        DependencyProperty.Register(nameof(Length), typeof(double),
            typeof(OpenSimplexPreview), new FrameworkPropertyMetadata(1d,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public int PointCount
    {
        get => (int)GetValue(PointCountProperty);
        set => SetValue(PointCountProperty, value);
    }

    public static readonly DependencyProperty PointCountProperty =
        DependencyProperty.Register(nameof(PointCount), typeof(int),
            typeof(OpenSimplexPreview), new FrameworkPropertyMetadata(100,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public int Octaves
    {
        get => (int)GetValue(OctavesProperty);
        set => SetValue(OctavesProperty, value);
    }

    public static readonly DependencyProperty OctavesProperty =
        DependencyProperty.Register(nameof(Octaves), typeof(int),
            typeof(OpenSimplexPreview), new FrameworkPropertyMetadata(1,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public double Persistence
    {
        get => (double)GetValue(PersistenceProperty);
        set => SetValue(PersistenceProperty, value);
    }

    public static readonly DependencyProperty PersistenceProperty =
        DependencyProperty.Register(nameof(Persistence), typeof(double),
            typeof(OpenSimplexPreview), new FrameworkPropertyMetadata(1d,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public double Lacunarity
    {
        get => (double)GetValue(LacunarityProperty);
        set => SetValue(LacunarityProperty, value);
    }

    public static readonly DependencyProperty LacunarityProperty =
        DependencyProperty.Register(nameof(Lacunarity), typeof(double),
            typeof(OpenSimplexPreview), new FrameworkPropertyMetadata(1d,
                new PropertyChangedCallback(OnPropertyChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not OpenSimplexPreview @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    public OpenSimplexPreview()
    {
        InitializeComponent();
        _noise = new OpenSimplex(Random.Shared.NextInt64());
    }

    private void Refresh()
    {
        void AddPoint(double x, double y)
            => Points.Add(new Point(x / Length * ActualWidth, (y + 1) / 2 * ActualHeight));

        Points = new PointCollection();

        var step = Length / PointCount;
        for (var x = 0d; x < Length; x += step)
            AddPoint(x, _noise.Calculate2D(x, _seed, Octaves, Persistence, Lacunarity));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _seed = Random.Shared.Next(short.MinValue, short.MaxValue);
        Refresh();
    }

    [SuppressPropertyChangedWarnings]
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();
}
