using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Controls.ViewModels;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for KeyframesHeatmap.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
internal sealed partial class KeyframesHeatmap : UserControl
{
    public static int MaxBucketCount => 500;

    private readonly HeatmapBucket[] _buckets;
    private readonly Color[] _colors;
    private int _selectedAxisIndex;

    public GradientStopCollection Stops { get; set; }
    public PointCollection Points { get; set; }

    public double ToolTipPositionOffset { get; set; }
    public bool ToolTipIsOpen { get; set; }
    public double ToolTipHorizontalOffset { get; set; }
    public double ToolTipVerticalOffset { get; set; }
    public KeyframeCollection ToolTipKeyframes { get; set; }
    public InterpolationType ToolTipInterpolationType { get; set; }

    public double ScrubberPosition => ShowScrubber ? Position / Duration * ActualWidth : 0;
    public bool ShowScrubber => double.IsFinite(Duration) && Duration > 0;
    public DeviceAxis SelectedAxis => DeviceAxis.All.ElementAt(_selectedAxisIndex);

    public event EventHandler<SeekRequestEventArgs> SeekRequest;

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, KeyframeCollection> Keyframes
    {
        get => (IReadOnlyDictionary<DeviceAxis, KeyframeCollection>)GetValue(KeyframesProperty);
        set => SetValue(KeyframesProperty, value);
    }

    public static readonly DependencyProperty KeyframesProperty =
        DependencyProperty.Register(nameof(Keyframes), typeof(IReadOnlyDictionary<DeviceAxis, KeyframeCollection>),
            typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnKeyframesChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnKeyframesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldKeyframes)
            oldKeyframes.CollectionChanged -= @this.OnKeyframesCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newKeyframes)
            newKeyframes.CollectionChanged += @this.OnKeyframesCollectionChanged;

        @this.Refresh();
    }

    [SuppressPropertyChangedWarnings]
    private void OnKeyframesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Refresh();

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, AxisSettings> Settings
    {
        get => (IReadOnlyDictionary<DeviceAxis, AxisSettings>)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public static readonly DependencyProperty SettingsProperty =
        DependencyProperty.Register(nameof(Settings), typeof(IReadOnlyDictionary<DeviceAxis, AxisSettings>),
            typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(null));

    [DoNotNotify]
    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(double),
            typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnDurationChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ShowScrubber)));
    }

    [DoNotNotify]
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double),
            typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnPositionChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ScrubberPosition)));
    }

    [DoNotNotify]
    public bool ShowRange
    {
        get => (bool)GetValue(ShowRangeProperty);
        set => SetValue(ShowRangeProperty, value);
    }

    public static readonly DependencyProperty ShowRangeProperty =
        DependencyProperty.Register(nameof(ShowRange), typeof(bool),
            typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(false,
                new PropertyChangedCallback(OnShowRangeChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnShowRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ShowRange)));
    }

    [DoNotNotify]
    public int BucketCount
    {
        get => (int)GetValue(BucketCountProperty);
        set => SetValue(BucketCountProperty, value);
    }

    public static readonly DependencyProperty BucketCountProperty =
       DependencyProperty.Register(nameof(BucketCount), typeof(int),
           typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(333,
               new PropertyChangedCallback(OnBucketCountChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnBucketCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(BucketCount)));
    }

    [DoNotNotify]
    public bool InvertY
    {
        get => (bool)GetValue(InvertYProperty);
        set => SetValue(InvertYProperty, value);
    }

    public static readonly DependencyProperty InvertYProperty =
       DependencyProperty.Register(nameof(InvertY), typeof(bool),
           typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(false,
               new PropertyChangedCallback(OnInvertYChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnInvertYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(InvertY)));
    }

    [DoNotNotify]
    public bool EnablePreview
    {
        get => (bool)GetValue(EnablePreviewProperty);
        set => SetValue(EnablePreviewProperty, value);
    }

    public static readonly DependencyProperty EnablePreviewProperty =
       DependencyProperty.Register(nameof(EnablePreview), typeof(bool),
           typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(false));

    [DoNotNotify]
    public bool CombineHeat
    {
        get => (bool)GetValue(CombineHeatProperty);
        set => SetValue(CombineHeatProperty, value);
    }

    public static readonly DependencyProperty CombineHeatProperty =
       DependencyProperty.Register(nameof(CombineHeat), typeof(bool),
           typeof(KeyframesHeatmap), new FrameworkPropertyMetadata(true,
               new PropertyChangedCallback(OnCombineHeatChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnCombineHeatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmap @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(CombineHeat)));
    }

    public KeyframesHeatmap()
    {
        _buckets = new HeatmapBucket[MaxBucketCount];
        _colors =
        [
            (Color)ColorConverter.ConvertFromString("#244b5c"),
            (Color)ColorConverter.ConvertFromString("#75b9d1"),
            (Color)ColorConverter.ConvertFromString("#efce62"),
            (Color)ColorConverter.ConvertFromString("#f39944"),
            (Color)ColorConverter.ConvertFromString("#f53e2e"),
        ];

        InitializeComponent();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateToolTip(true);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateToolTip(true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateToolTip(false);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        if (!double.IsFinite(Duration) || Duration <= 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        if (e.LeftButton == MouseButtonState.Pressed)
            SeekRequest?.Invoke(this, new SeekRequestEventArgs(TimeSpan.FromSeconds(e.GetPosition(this).X / ActualWidth * Duration)));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        _selectedAxisIndex = Math.Clamp(_selectedAxisIndex - Math.Sign(e.Delta), 0, DeviceAxis.All.Count - 1);

        Refresh();
        if (ToolTipIsOpen)
            UpdateToolTipKeyframes();
    }

    private void UpdateToolTip(bool open)
    {
        if (!double.IsFinite(Duration) || Duration <= 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        if (!ToolTipIsOpen && open)
            UpdateToolTipKeyframes();

        ToolTipIsOpen = open;
        if (open)
        {
            var x = Mouse.GetPosition(this).X;
            var y = Mouse.GetPosition(this).Y;
            ToolTipHorizontalOffset = x - (EnablePreview ? 100 : 40);
            ToolTipVerticalOffset = y - (EnablePreview ? 100 : 40);
            ToolTipPositionOffset = x / (double)ActualWidth * Duration;
        }
    }

    private void UpdateToolTipKeyframes()
    {
        ToolTipKeyframes = Keyframes.TryGetValue(SelectedAxis, out var keyframes) ? keyframes : null;
        ToolTipInterpolationType = SelectedAxis != null ? Settings[SelectedAxis].InterpolationType : InterpolationType.Linear;
    }

    private void Refresh()
    {
        Stops = [];
        Points = [];

        if (Keyframes == null || Keyframes.Count == 0 || !double.IsFinite(Duration) || Duration <= 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        var bucketSize = Duration / BucketCount;
        var buckets = _buckets.AsSpan(0, (int)Math.Floor(Duration / bucketSize));

        for (var i = 0; i < buckets.Length; i++)
            buckets[i].Clear();

        UpdateHeat(buckets, bucketSize);
        UpdateRange(buckets, bucketSize);
    }

    private void UpdateHeat(Span<HeatmapBucket> buckets, double bucketSize)
    {
        if (!CombineHeat)
        {
            if (SelectedAxis != null && Keyframes.TryGetValue(SelectedAxis, out var keyframes))
                UpdateHeat(buckets, keyframes);
        }
        else
        {
            foreach (var (_, keyframes) in Keyframes)
                UpdateHeat(buckets, keyframes);
        }

        AddStop(Color.FromRgb(0, 0, 0), 0);

        var maxLength = buckets.Length > 0 ? buckets[0].TotalLength : 0;
        for (var i = 1; i < buckets.Length; i++)
            maxLength = Math.Max(maxLength, buckets[i].TotalLength);

        var normalizationFactor = 1d / maxLength;
        if (double.IsFinite(normalizationFactor))
        {
            for (var i = 0; i < buckets.Length; i++)
            {
                var heat = MathUtils.Clamp01(buckets[i].TotalLength * normalizationFactor);
                var color = heat < 0.001 ? Color.FromRgb(0, 0, 0) : _colors[(int)Math.Round(heat * (_colors.Length - 1))];

                AddStop(color, i * bucketSize / Duration);
                if (i < buckets.Length - 1)
                    AddStop(color, (i + 1) * bucketSize / Duration);
            }
        }

        AddStop(Color.FromRgb(0, 0, 0), 1);

        void AddStop(Color color, double offset) => Stops.Add(new GradientStop(color, offset));

        void UpdateHeat(Span<HeatmapBucket> buckets, KeyframeCollection keyframes)
        {
            if (keyframes == null || keyframes.Count < 2)
                return;

            for (int i = 0, j = 1; j < keyframes.Count; i = j++)
            {
                var prev = keyframes[i];
                var next = keyframes[j];

                if (next.Position < 0 || prev.Position < 0)
                    continue;

                var dx = next.Position - prev.Position;
                var dy = next.Value - prev.Value;
                var adx = Math.Abs(dx);
                var ady = Math.Abs(dy);
                if (ady < 0.001 || adx < 0.001 || Math.Atan2(ady, adx) * 180 / MathF.PI < 5)
                    continue;

                var startBucket = (int)Math.Floor(prev.Position / bucketSize);
                var endBucket = (int)Math.Floor(next.Position / bucketSize);

                for (var index = startBucket; index < buckets.Length && index <= endBucket; index++)
                {
                    var positionFrom = Math.Max(index * bucketSize, prev.Position);
                    var positionTo = Math.Min((index + 1) * bucketSize, next.Position);

                    buckets[index].TotalLength += ady * (positionTo - positionFrom) / adx;
                }
            }
        }
    }

    private void UpdateRange(Span<HeatmapBucket> buckets, double bucketSize)
    {
        void AddPoint(double x, double y)
            => Points.Add(new Point(double.IsFinite(x) ? x : 0, double.IsFinite(y) ? y : 0));

        void AddPointForBucket(int index, double value)
            => AddPoint(index * bucketSize / Duration * ActualWidth, MathUtils.Clamp01(!InvertY ? 1 - value : value) * ActualHeight);

        if (!ShowRange || !Keyframes.TryGetValue(SelectedAxis, out var keyframes) || keyframes == null || keyframes.Count < 2)
        {
            AddPoint(0, 0);
            AddPoint(ActualWidth, 0);
            AddPoint(ActualWidth, ActualHeight);
            AddPoint(0, ActualHeight);

            return;
        }

        for (int i = 0, j = 1; j < keyframes.Count; i = j++)
        {
            var prev = keyframes[i];
            var next = keyframes[j];
            var direction = Math.Sign(keyframes[j].Value - keyframes[i].Value);

            var startBucket = (int)Math.Floor(prev.Position / bucketSize);
            var endBucket = (int)Math.Floor(next.Position / bucketSize);
            for (var index = startBucket; index < buckets.Length && index <= endBucket; index++)
            {
                var positionFrom = Math.Max(index * bucketSize, prev.Position);
                var positionTo = Math.Min((index + 1) * bucketSize, next.Position);
                var valueFrom = MathUtils.Map(positionFrom, prev.Position, next.Position, prev.Value, next.Value);
                var valueTo = MathUtils.Map(positionTo, prev.Position, next.Position, prev.Value, next.Value);

                if (direction > 0)
                {
                    buckets[index].Bottom.Add(valueFrom);
                    buckets[index].Top.Add(valueTo);
                }
                else if (direction < 0)
                {
                    buckets[index].Top.Add(valueFrom);
                    buckets[index].Bottom.Add(valueTo);
                }
                else
                {
                    buckets[index].Top.Add((valueFrom + valueTo) / 2);
                    buckets[index].Bottom.Add((valueFrom + valueTo) / 2);
                }
            }
        }

        for (var i = 0; i < buckets.Length; i++)
            AddPointForBucket(i, buckets[i].Top.Count > 0 ? buckets[i].Top.Average : SelectedAxis.DefaultValue);

        for (var i = buckets.Length - 1; i >= 0; i--)
            AddPointForBucket(i, buckets[i].Bottom.Count > 0 ? buckets[i].Bottom.Average : SelectedAxis.DefaultValue);

        AddPointForBucket(0, buckets[0].Top.Count > 0 ? buckets[0].Top.Average : SelectedAxis.DefaultValue);
    }

    private struct HeatmapBucket
    {
        public double TotalLength;
        public HeatmapBucketValue Top;
        public HeatmapBucketValue Bottom;

        public void Clear()
        {
            TotalLength = 0;
            Top = new();
            Bottom = new();
        }

        public struct HeatmapBucketValue
        {
            public double Total { get; private set; }
            public int Count { get; private set; }
            public readonly double Average => Total / Count;

            public void Add(double value)
            {
                Total += value;
                Count++;
            }
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    [SuppressPropertyChangedWarnings]
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();
}
