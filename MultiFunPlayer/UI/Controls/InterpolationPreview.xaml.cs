using MultiFunPlayer.Common;
using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;
/// <summary>
/// Interaction logic for InterpolationPreview.xaml
/// </summary>
public partial class InterpolationPreview : UserControl, INotifyPropertyChanged
{
    private KeyframeCollection _keyframes;

    public PointCollection CurvePoints { get; set; }
    public ObservableCollection<Point> Points { get; set; }

    [DoNotNotify]
    public InterpolationType InterpolationType
    {
        get => (InterpolationType)GetValue(InterpolationTypeProperty);
        set => SetValue(InterpolationTypeProperty, value);
    }

    public static readonly DependencyProperty InterpolationTypeProperty =
        DependencyProperty.Register(nameof(InterpolationType), typeof(InterpolationType),
            typeof(InterpolationPreview), new FrameworkPropertyMetadata(InterpolationType.Linear,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public int CurvePointCount
    {
        get => (int)GetValue(CurvePointCountProperty);
        set => SetValue(CurvePointCountProperty, value);
    }

    public static readonly DependencyProperty CurvePointCountProperty =
        DependencyProperty.Register(nameof(CurvePointCount), typeof(int),
            typeof(InterpolationPreview), new FrameworkPropertyMetadata(100,
                new PropertyChangedCallback(OnPropertyChanged)));

    [DoNotNotify]
    public int PointCount
    {
        get => (int)GetValue(PointCountProperty);
        set => SetValue(PointCountProperty, value);
    }

    public static readonly DependencyProperty PointCountProperty =
        DependencyProperty.Register(nameof(PointCount), typeof(int),
            typeof(InterpolationPreview), new FrameworkPropertyMetadata(8,
                new PropertyChangedCallback(OnPropertyChanged)));

    public InterpolationPreview()
    {
        InitializeComponent();
    }

    [SuppressPropertyChangedWarnings]
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InterpolationPreview @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    private void GenerateKeyframes()
    {
        void AddKeyframe(float x, float y)
        {
            Points.Add(new Point(ActualWidth * x, ActualHeight * y));
            _keyframes.Add(new Keyframe(x, y));
        }

        Points = new ObservableCollection<Point>();
        _keyframes = new KeyframeCollection();

        AddKeyframe(0, 0.5f);
        while (_keyframes.Count < PointCount - 1)
        {
            var y = MathUtils.Clamp01((int)Math.Round(Random.Shared.NextDouble() * 5) / 5f);
            if (y == _keyframes.Last().Value)
                continue;

            for(var i = 0; i < 2; i++)
                if (_keyframes.Count != PointCount - 1)
                    AddKeyframe(_keyframes.Count / (PointCount - 1f), y);
        }
        AddKeyframe(1, 0.5f);
    }

    private void Refresh()
    {
        void AddPoint(float x, float y)
            => CurvePoints.Add(new Point(ActualWidth * x, ActualHeight * y));

        if (_keyframes == null || _keyframes.Count == 0)
            return;

        CurvePoints = new PointCollection();

        var step = 1f / CurvePointCount;
        for (var i = 0; i < _keyframes.Count - 1; i++)
            for (var x = _keyframes[i].Position; x < _keyframes[i + 1].Position; x += step)
                AddPoint(x, MathUtils.Clamp01(_keyframes.Interpolate(i, x, InterpolationType)));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        GenerateKeyframes();
        Refresh();
    }

    [SuppressPropertyChangedWarnings]
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_keyframes == null)
            GenerateKeyframes();

        Refresh();
    }
}
