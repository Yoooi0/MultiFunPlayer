using MultiFunPlayer.Common;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for KeyframesHeatmapGradient.xaml
/// </summary>
public partial class KeyframesHeatmapGradient : UserControl, INotifyPropertyChanged
{
    public static int MaxBucketCount => 500;

    private readonly HeatmapBucket[] _buckets;
    private readonly Color[] _colors;

    public GradientStopCollection Stops { get; set; }
    public PointCollection Points { get; set; }

    public float ScrubberPosition => ShowScrubber ? Position / Duration * (float)ActualWidth : 0;
    public bool ShowScrubber => float.IsFinite(Duration) && Duration > 0;

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, KeyframeCollection> Keyframes
    {
        get => (IReadOnlyDictionary<DeviceAxis, KeyframeCollection>)GetValue(KeyframesProperty);
        set => SetValue(KeyframesProperty, value);
    }

    public static readonly DependencyProperty KeyframesProperty =
        DependencyProperty.Register(nameof(Keyframes), typeof(IReadOnlyDictionary<DeviceAxis, KeyframeCollection>),
            typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnKeyframesChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnKeyframesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapGradient @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldKeyframes)
            oldKeyframes.CollectionChanged -= @this.OnKeyframesCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newKeframes)
            newKeframes.CollectionChanged += @this.OnKeyframesCollectionChanged;

        @this.Refresh();
    }

    [SuppressPropertyChangedWarnings]
    private void OnKeyframesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Refresh();

    [DoNotNotify]
    public float Duration
    {
        get => (float)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(float),
            typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(float.NaN,
                new PropertyChangedCallback(OnDurationChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapGradient @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ShowScrubber)));
    }

    [DoNotNotify]
    public float Position
    {
        get => (float)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(float),
            typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(float.NaN,
                new PropertyChangedCallback(OnPositionChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapGradient @this)
            return;

        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ScrubberPosition)));
    }

    [DoNotNotify]
    public bool ShowStrokeLength
    {
        get => (bool)GetValue(ShowStrokeLengthProperty);
        set => SetValue(ShowStrokeLengthProperty, value);
    }

    public static readonly DependencyProperty ShowStrokeLengthProperty =
        DependencyProperty.Register(nameof(ShowStrokeLength), typeof(bool),
            typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(false,
                new PropertyChangedCallback(OnShowStrokeLengthChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnShowStrokeLengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapGradient @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ShowStrokeLength)));
    }

    [DoNotNotify]
    public int BucketCount
    {
        get => (int)GetValue(BucketCountProperty);
        set => SetValue(BucketCountProperty, value);
    }

    public static readonly DependencyProperty BucketCountProperty =
       DependencyProperty.Register(nameof(BucketCount), typeof(int),
           typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(333,
               new PropertyChangedCallback(OnBucketCountChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnBucketCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyframesHeatmapGradient @this)
            return;

        @this.Refresh();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(BucketCount)));
    }

    public KeyframesHeatmapGradient()
    {
        _buckets = new HeatmapBucket[MaxBucketCount];
        _colors = new Color[]
        {
                Color.FromRgb(0x24, 0x4b, 0x5c),
                Color.FromRgb(0x75, 0xb9, 0xd1),
                Color.FromRgb(0xef, 0xce, 0x62),
                Color.FromRgb(0xf3, 0x99, 0x44),
                Color.FromRgb(0xf5, 0x3e, 0x2e),
        };

        InitializeComponent();
    }

    private void Refresh()
    {
        Stops = new GradientStopCollection();
        Points = new PointCollection();
        if (Keyframes == null || Keyframes.Count == 0 || !float.IsFinite(Duration) || Duration <= 0)
            return;

        var bucketSize = Duration / BucketCount;
        var buckets = _buckets.AsSpan(0, (int)MathF.Floor(Duration / bucketSize));

        for (var i = 0; i < buckets.Length; i++)
            buckets[i].Clear();

        UpdateHeat(buckets, bucketSize);
        if (ShowStrokeLength)
            UpdateStroke(buckets, bucketSize);
    }

    private void UpdateHeat(Span<HeatmapBucket> buckets, float bucketSize)
    {
        foreach (var (_, keyframes) in Keyframes)
        {
            if (keyframes == null || keyframes.Count < 2)
                continue;

            for (int i = 0, j = 1; j < keyframes.Count; i = j++)
            {
                var prev = keyframes[i];
                var next = keyframes[j];

                if (next.Position < 0 || prev.Position < 0)
                    continue;

                var dx = next.Position - prev.Position;
                var dy = next.Value - prev.Value;
                var adx = MathF.Abs(dx);
                var ady = MathF.Abs(dy);
                if (ady < 0.001f || adx < 0.001f || MathF.Atan2(ady, adx) * 180 / MathF.PI < 5)
                    continue;

                var startBucket = (int)MathF.Floor(prev.Position / bucketSize);
                var endBucket = (int)MathF.Floor(next.Position / bucketSize);

                for (var index = startBucket; index < buckets.Length && index <= endBucket; index++)
                {
                    var positionFrom = MathF.Max(index * bucketSize, prev.Position);
                    var positionTo = MathF.Min((index + 1) * bucketSize, next.Position);

                    buckets[index].Length.Add(ady * (positionTo - positionFrom) / adx);
                }
            }
        }

        void AddStop(Color color, float offset) => Stops.Add(new GradientStop(color, offset));

        AddStop(Color.FromRgb(0, 0, 0), 0);

        var maxLength = float.NaN;
        for (var i = 0; i < buckets.Length; i++)
        {
            var value = buckets[i].Length.Average;
            if (!float.IsFinite(value))
                continue;

            maxLength = !float.IsFinite(maxLength) ? value : MathF.Max(maxLength, value);
        }

        var normalizationFactor = 1.0f / maxLength;
        if (float.IsFinite(normalizationFactor))
        {
            for (var i = 0; i < buckets.Length; i++)
            {
                var heat = buckets[i].Length.Count > 0 ? MathUtils.Clamp01(buckets[i].Length.Average * normalizationFactor) : 0;
                var color = heat < 0.001f ? Color.FromRgb(0, 0, 0) : _colors[(int)MathF.Round(heat * (_colors.Length - 1))];

                AddStop(color, i * bucketSize / Duration);
                if (i < buckets.Length - 1)
                    AddStop(color, (i + 1) * bucketSize / Duration);
            }

            AddStop(Color.FromRgb(0, 0, 0), 1);
        }
    }

    private void UpdateStroke(Span<HeatmapBucket> buckets, float bucketSize)
    {
        if (!DeviceAxis.TryParse("L0", out var axis) || !Keyframes.TryGetValue(axis, out var keyframes))
            return;

        if (keyframes == null || keyframes.Count < 2)
            return;

        for (var i = 0; i < keyframes.Count - 1;)
        {
            var j = i;
            var direction = default(int?);
            for (var k = j + 1; k < keyframes.Count; j = k++)
            {
                var currentDirection = MathF.Sign(keyframes[k].Value - keyframes[j].Value);

                if (!direction.HasValue)
                    direction = currentDirection;

                if (direction.HasValue && direction != currentDirection)
                    break;
            }

            var prev = keyframes[i];
            var next = keyframes[j];

            var startBucket = (int)MathF.Floor(prev.Position / bucketSize);
            var endBucket = (int)MathF.Floor(next.Position / bucketSize);

            if (startBucket >= buckets.Length)
                break;

            if (direction > 0) buckets[startBucket].Bottom.Add(prev.Value);
            else if (direction < 0) buckets[startBucket].Top.Add(prev.Value);
            else if (prev.Value > axis.DefaultValue) buckets[startBucket].Top.Add(prev.Value);
            else buckets[startBucket].Bottom.Add(prev.Value);

            if (endBucket < buckets.Length)
            {
                if (direction > 0) buckets[endBucket].Top.Add(next.Value);
                else if (direction < 0) buckets[endBucket].Bottom.Add(next.Value);
                else if (next.Value > axis.DefaultValue) buckets[endBucket].Top.Add(next.Value);
                else buckets[endBucket].Bottom.Add(next.Value);
            }

            i = j;
        }

        void AddPoint(float x, float y)
            => Points.Add(new Point(float.IsFinite(x) ? x : 0, float.IsFinite(y) ? y : 0));

        void AddPointForBucket(int index, float value)
            => AddPoint(index * bucketSize / Duration * (float)ActualWidth, (1 - value) * (float)ActualHeight);

        for (var i = 0; i < buckets.Length; i++)
            AddPointForBucket(i, buckets[i].Top.Count > 0 ? buckets[i].Top.Average : axis.DefaultValue);

        for (var i = buckets.Length - 1; i >= 0; i--)
            AddPointForBucket(i, buckets[i].Bottom.Count > 0 ? buckets[i].Bottom.Average : axis.DefaultValue);

        AddPointForBucket(0, buckets[0].Top.Count > 0 ? buckets[0].Top.Average : axis.DefaultValue);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private struct HeatmapBucket
    {
        public HeatmapBucketValue Length;
        public HeatmapBucketValue Top;
        public HeatmapBucketValue Bottom;

        public void Clear()
        {
            Length = new();
            Top = new();
            Bottom = new();
        }

        public struct HeatmapBucketValue
        {
            public float Total { get; private set; }
            public int Count { get; private set; }
            public float Average => Total / Count;

            public void Add(float value)
            {
                Total += value;
                Count++;
            }
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
