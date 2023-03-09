using MultiFunPlayer.Common;
using PropertyChanged;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for KeyframesHeatmapToolTip.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
internal partial class KeyframesHeatmapToolTip : UserControl
{
    public PointCollection Points { get; set; }
    public double? ScrubberPosition { get; set; }

    public double CanvasHeight => 50;
    public double CanvasWidth => 200;
    public TimeSpan PositionTime => TimeSpan.FromSeconds(PositionOffset);

    [DoNotNotify]
    public KeyframeCollection Keyframes
    {
        get => (KeyframeCollection)GetValue(KeyframesProperty);
        set => SetValue(KeyframesProperty, value);
    }

    public static readonly DependencyProperty KeyframesProperty =
        DependencyProperty.Register(nameof(Keyframes), typeof(KeyframeCollection),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnKeyframesChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnKeyframesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        @this.Refresh(true, false);
    }

    [DoNotNotify]
    public InterpolationType InterpolationType
    {
        get => (InterpolationType)GetValue(InterpolationTypeProperty);
        set => SetValue(InterpolationTypeProperty, value);
    }

    public static readonly DependencyProperty InterpolationTypeProperty =
        DependencyProperty.Register(nameof(InterpolationType), typeof(InterpolationType),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(InterpolationType.Linear,
                new PropertyChangedCallback(OnInterpolationTypeChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnInterpolationTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        @this.Refresh(true, false);
    }

    [DoNotNotify]
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnPositionChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        @this.Refresh(false, true);
    }

    [DoNotNotify]
    public double PositionOffset
    {
        get => (double)GetValue(PositionOffsetProperty);
        set => SetValue(PositionOffsetProperty, value);
    }

    public static readonly DependencyProperty PositionOffsetProperty =
        DependencyProperty.Register(nameof(PositionOffset), typeof(double),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(0.0,
                new PropertyChangedCallback(OnPositionOffsetChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPositionOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        @this.Refresh(true, true);
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(PositionTime)));
    }

    [DoNotNotify]
    public bool EnablePreview
    {
        get => (bool)GetValue(EnablePreviewProperty);
        set => SetValue(EnablePreviewProperty, value);
    }

    public static readonly DependencyProperty EnablePreviewProperty =
       DependencyProperty.Register(nameof(EnablePreview), typeof(bool),
           typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(false));

    public KeyframesHeatmapToolTip()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Refresh(true, true);
        SizeChanged += (_, _) => Refresh(true, true);
    }

    private void Refresh(bool refreshPoints, bool refreshScrubber)
    {
        if (refreshPoints)
            Points = new PointCollection();

        if (refreshScrubber)
            ScrubberPosition = null;

        if (!IsVisible || !EnablePreview)
            return;

        if (Keyframes == null || Keyframes.Count == 0 || ActualWidth < 1 || ActualHeight < 1 || !double.IsFinite(PositionOffset))
            return;

        const double duration = 8;
        var startPosition = PositionOffset - duration / 2;
        var endPosition = PositionOffset + duration / 2;

        if (refreshPoints)
            RefreshPoints(startPosition, endPosition, duration, Keyframes, InterpolationType);

        if (refreshScrubber)
            RefreshScrubber(duration, startPosition, endPosition);
    }

    private void RefreshScrubber(double duration, double startPosition, double endPosition)
    {
        if (Position >= startPosition || Position <= endPosition)
            ScrubberPosition = (Position - startPosition) / duration * CanvasWidth;
    }

    private void RefreshPoints(double startPosition, double endPosition, double duration, KeyframeCollection keyframes, InterpolationType interpolationType)
    {
        const int pointCount = 300;

        var index = -1;
        for (var i = 0; i < pointCount; i++)
        {
            var t = i / (double)pointCount;
            var position = MathUtils.Lerp(startPosition, endPosition, t);

            index = index == -1 ? keyframes.SearchForIndexBefore(position) : keyframes.AdvanceIndex(index, position);

            var value = keyframes.ValidateIndex(index) && keyframes.ValidateIndex(index + 1)
                ? MathUtils.Clamp01(keyframes.Interpolate(index, position, interpolationType))
                : double.NaN;

            if (!double.IsFinite(value))
                continue;

            var x = (position - startPosition) / duration * CanvasWidth;
            var y = (1 - value) * CanvasHeight;

            Points.Add(new Point(x, y));
        }
    }
}
