using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Controls.ViewModels;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for KeyframesHeatmapToolTip.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
public partial class KeyframesHeatmapToolTip : UserControl
{
    public PointCollection Points { get; set; }
    public double? ScrubberPosition { get; set; }

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, KeyframeCollection> Keyframes
    {
        get => (IReadOnlyDictionary<DeviceAxis, KeyframeCollection>)GetValue(KeyframesProperty);
        set => SetValue(KeyframesProperty, value);
    }

    public static readonly DependencyProperty KeyframesProperty =
        DependencyProperty.Register(nameof(Keyframes), typeof(IReadOnlyDictionary<DeviceAxis, KeyframeCollection>),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnKeyframesChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnKeyframesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldKeyframes)
            oldKeyframes.CollectionChanged -= @this.OnKeyframesCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newKeyframes)
            newKeyframes.CollectionChanged += @this.OnKeyframesCollectionChanged;

        @this.Refresh(true, false);
    }

    [SuppressPropertyChangedWarnings]
    private void OnKeyframesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Refresh(true, false);

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, AxisSettings> Settings
    {
        get => (IReadOnlyDictionary<DeviceAxis, AxisSettings>)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public static readonly DependencyProperty SettingsProperty =
        DependencyProperty.Register(nameof(Settings), typeof(IReadOnlyDictionary<DeviceAxis, AxisSettings>),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnSettingsChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldKeyframes)
            oldKeyframes.CollectionChanged -= @this.OnSettingsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newKeyframes)
            newKeyframes.CollectionChanged += @this.OnSettingsCollectionChanged;

        @this.Refresh(true, false);
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh(true, false);

        if (e.OldItems != null)
            foreach(var item in e.OldItems.OfType<AxisSettings>())
                item.PropertyChanged -= OnSettingsPropertyChanged;

        if (e.NewItems != null)
            foreach (var item in e.NewItems.OfType<AxisSettings>())
                item.PropertyChanged += OnSettingsPropertyChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AxisSettings.InterpolationType))
            Refresh(true, false);
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
    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public static readonly DependencyProperty OffsetProperty =
        DependencyProperty.Register(nameof(Offset), typeof(double),
            typeof(KeyframesHeatmapToolTip), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnOffsetChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapToolTip @this)
            return;

        @this.Refresh(true, true);
    }

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

        if (!IsVisible)
            return;

        if (Keyframes == null || Keyframes.Count == 0 || ActualWidth < 1 || ActualHeight < 1 || !double.IsFinite(Offset))
            return;

        if (!DeviceAxis.TryParse("L0", out var axis))
            return;

        var keyframes = Keyframes[axis];
        if (keyframes == null)
            return;

        const double duration = 4;
        var startPosition = Offset - duration / 2;
        var endPosition = Offset + duration / 2;

        if (refreshPoints)
        {
            var interpolationType = Settings.TryGetValue(axis, out var settings) ? settings.InterpolationType : InterpolationType.Linear;
            RefreshPoints(startPosition, endPosition, duration, keyframes, interpolationType);
        }

        if (refreshScrubber)
            RefreshScrubber(duration, startPosition, endPosition);
    }

    private void RefreshScrubber(double duration, double startPosition, double endPosition)
    {
        if (Position >= startPosition || Position <= endPosition)
            ScrubberPosition = (Position - startPosition) / duration * ActualWidth;
    }

    private void RefreshPoints(double startPosition, double endPosition, double duration, KeyframeCollection keyframes, InterpolationType interpolationType)
    {
        var index = -1;
        for (var i = 0; i < 300; i++)
        {
            var t = i / (double)300;
            var position = MathUtils.Lerp(startPosition, endPosition, t);

            var value = double.NaN;
            if (index == -1)
            {
                value = MathUtils.Clamp01(keyframes.Interpolate(position, interpolationType, out index));
            }
            else
            {
                index = keyframes.AdvanceIndex(index, position);
                if (keyframes.ValidateIndex(index) && keyframes.ValidateIndex(index + 1))
                    value = MathUtils.Clamp01(keyframes.Interpolate(index, position, interpolationType));
            }

            if (!double.IsFinite(value))
                continue;

            var x = (position - startPosition) / duration * ActualWidth;
            var y = (1 - value) * ActualHeight;

            Points.Add(new Point(x, y));
        }
    }
}
