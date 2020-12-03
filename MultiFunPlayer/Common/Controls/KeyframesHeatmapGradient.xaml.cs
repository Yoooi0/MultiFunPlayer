using MultiFunPlayer.ViewModels;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for KeyframesHeatmapGradient.xaml
    /// </summary>
    public partial class KeyframesHeatmapGradient : UserControl, INotifyPropertyChanged
    {
        public GradientStopCollection Stops { get; set; }
        public float ScrubberPosition => float.IsFinite(Duration) && Duration > 0 ? Position / Duration * (float)ActualWidth : float.NegativeInfinity;

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

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not KeyframesHeatmapGradient @this)
                return;

            @this.Refresh();
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

            var duration = Math.Max(Duration, Keyframes.SelectMany(x => x.Value).Max(x => x.Position));
            var bucketSize = 5f;

            var colors = new Color[]
            {
                Color.FromRgb(0x24, 0x4b, 0x5c),
                Color.FromRgb(0x75, 0xb9, 0xd1),
                Color.FromRgb(0xef, 0xce, 0x62),
                Color.FromRgb(0xf3, 0x99, 0x44),
                Color.FromRgb(0xf5, 0x3e, 0x2e),
            };

            var buckets = new float[(int) Math.Ceiling(duration / bucketSize)];

            AddStop(Color.FromRgb(0, 0, 0), 0);
            foreach (var (axis, keyframes) in Keyframes)
            {
                var startTime = keyframes.First().Position;
                var endTime = keyframes.Last().Position;

                for (int i = 0, j = 1; j < keyframes.Count; i = j++)
                {
                    var prev = keyframes[i];
                    var next = keyframes[j];

                    var dx = next.Position - prev.Position;
                    var dy = next.Value - prev.Value;
                    if (Math.Abs(dx) < 0.001f || Math.Abs(dy) < 0.001f)
                        continue;

                    var length = (float)Math.Sqrt(dx * dx + dy * dy);
                    var tangent = Math.Abs(dy / dx * (float)Math.PI / 180);

                    if (!float.IsFinite(length) || !float.IsFinite(tangent))
                        continue;

                    var bucket = (int)Math.Floor(((prev.Position + next.Position) / 2) / bucketSize);
                    //while (bucket >= buckets.Count)
                    //    buckets.Add(0);

                    buckets[bucket] += length;
                }
            }

            var normalizationFactor = 1.0f / buckets.Max();
            for (var i = 0; i < buckets.Length; i++)
            {
                var heat = MathUtils.Clamp01(buckets[i] * normalizationFactor);
                var color = heat < 0.001f ? Color.FromRgb(0, 0, 0) : colors[(int)Math.Round(heat * (colors.Length - 1))];
                //var offset = MathUtils.Lerp(0, buckets.Count * bucketSize, buckets.Count == 1 ? 0 : i / (buckets.Count - 1.0f)) / duration;
                AddStop(color, i * bucketSize / duration);
                if(i < buckets.Length - 1)
                    AddStop(color, (i + 1) * bucketSize / duration);
            }

            AddStop(Color.FromRgb(0, 0, 0), 1);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
