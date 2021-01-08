using MultiFunPlayer.ViewModels;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for KeyframesHeatmapGradient.xaml
    /// </summary>
    public partial class KeyframesHeatmapGradient : UserControl, INotifyPropertyChanged
    {
        public GradientStopCollection Stops { get; set; }

        public float ScrubberPosition => ShowScrubber ? Position / Duration * (float)ActualWidth : 0;
        public bool ShowScrubber => float.IsFinite(Duration) && Duration > 0;

        [DoNotNotify]
        public ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>> Keyframes
        {
            get { return (ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>>)GetValue(KeyframesProperty); }
            set { SetValue(KeyframesProperty, value); }
        }

        public static readonly DependencyProperty KeyframesProperty =
            DependencyProperty.Register("Keyframes", typeof(ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>>),
                typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(null,
                    new PropertyChangedCallback(OnKeyframesChanged)));

        [SuppressPropertyChangedWarnings]
        private static void OnKeyframesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not KeyframesHeatmapGradient @this)
                return;

            if (e.OldValue is ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>> oldKeyframes)
                oldKeyframes.CollectionChanged -= @this.OnKeyframesCollectionChanged;
            if (e.NewValue is ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>> newKeframes)
                newKeframes.CollectionChanged += @this.OnKeyframesCollectionChanged;

            @this.Refresh();
        }

        [SuppressPropertyChangedWarnings]
        private void OnKeyframesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Refresh();

        [DoNotNotify]
        public float Duration
        {
            get { return (float)GetValue(DurationProperty); }
            set { SetValue(DurationProperty, value); }
        }

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register("Duration", typeof(float),
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
            get { return (float)GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register("Position", typeof(float),
                typeof(KeyframesHeatmapGradient), new FrameworkPropertyMetadata(float.NaN,
                    new PropertyChangedCallback(OnPositionChanged)));

        [SuppressPropertyChangedWarnings]
        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not KeyframesHeatmapGradient @this)
                return;

            @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ScrubberPosition)));
        }

        public KeyframesHeatmapGradient()
        {
            InitializeComponent();
        }

        private void Refresh()
        {
            Stops = new GradientStopCollection();
            if (Keyframes?.Count() == 0 || !float.IsFinite(Duration) || Duration <= 0)
                return;

            void AddStop(Color color, float offset) => Stops.Add(new GradientStop(color, offset));

            var duration = MathF.Max(Duration, Keyframes.SelectMany(x => x.Value).Max(x => x.Position));
            const int bucketCount = 333;
            var bucketSize = (int)MathF.Ceiling(duration / bucketCount);

            var colors = new Color[]
            {
                Color.FromRgb(0x24, 0x4b, 0x5c),
                Color.FromRgb(0x75, 0xb9, 0xd1),
                Color.FromRgb(0xef, 0xce, 0x62),
                Color.FromRgb(0xf3, 0x99, 0x44),
                Color.FromRgb(0xf5, 0x3e, 0x2e),
            };

            var buckets = new float[(int)MathF.Ceiling(duration / bucketSize)];

            AddStop(Color.FromRgb(0, 0, 0), 0);
            foreach (var (axis, keyframes) in Keyframes)
            {
                var startTime = keyframes[0].Position;
                var endTime = keyframes[^1].Position;

                for (int i = 0, j = 1; j < keyframes.Count; i = j++)
                {
                    var prev = keyframes[i];
                    var next = keyframes[j];

                    if (next.Position < 0 || prev.Position < 0)
                        continue;

                    var dx = next.Position - prev.Position;
                    var dy = next.Value - prev.Value;
                    if (MathF.Abs(dx) < 0.001f || MathF.Abs(dy) < 0.001f)
                        continue;

                    var length = MathF.Sqrt(dx * dx + dy * dy);

                    var startBucket = (int)MathF.Floor(prev.Position / bucketSize);
                    var endBucket = (int)MathF.Floor(next.Position / bucketSize);

                    for (var bucket = startBucket; bucket <= endBucket; bucket++)
                        buckets[bucket] += length / (endBucket - startBucket + 1);
                }
            }

            var normalizationFactor = 1.0f / buckets.Max();
            if (float.IsFinite(normalizationFactor))
            {
                for (var i = 0; i < buckets.Length; i++)
                {
                    var heat = MathUtils.Clamp01(buckets[i] * normalizationFactor);
                    var color = heat < 0.001f ? Color.FromRgb(0, 0, 0) : colors[(int)MathF.Round(heat * (colors.Length - 1))];
                    AddStop(color, i * bucketSize / duration);
                    if (i < buckets.Length - 1)
                        AddStop(color, (i + 1) * bucketSize / duration);
                }
            }

            AddStop(Color.FromRgb(0, 0, 0), 1);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
