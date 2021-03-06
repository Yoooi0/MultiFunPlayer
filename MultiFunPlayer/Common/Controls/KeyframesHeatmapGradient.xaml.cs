﻿using PropertyChanged;
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

        public KeyframesHeatmapGradient()
        {
            InitializeComponent();
        }

        private void Refresh()
        {
            Stops = new GradientStopCollection();
            if (Keyframes == null || Keyframes.Count == 0 || !float.IsFinite(Duration) || Duration <= 0)
                return;

            void AddStop(Color color, float offset) => Stops.Add(new GradientStop(color, offset));

            const int bucketCount = 333;
            var bucketSize = (int)MathF.Ceiling(Duration / bucketCount);

            var colors = new Color[]
            {
                Color.FromRgb(0x24, 0x4b, 0x5c),
                Color.FromRgb(0x75, 0xb9, 0xd1),
                Color.FromRgb(0xef, 0xce, 0x62),
                Color.FromRgb(0xf3, 0x99, 0x44),
                Color.FromRgb(0xf5, 0x3e, 0x2e),
            };

            var buckets = new float[(int)MathF.Ceiling(Duration / bucketSize)];

            AddStop(Color.FromRgb(0, 0, 0), 0);
            foreach (var (axis, keyframes) in Keyframes)
            {
                if (keyframes == null || keyframes.Count < 2)
                    continue;

                var startTime = keyframes[0].Position;
                var endTime = keyframes[^1].Position;

                for (int i = 0, j = 1; j < keyframes.Count; i = j++)
                {
                    var prev = keyframes[i];
                    var next = keyframes[j];

                    if (next.Position < 0 || prev.Position < 0)
                        continue;

                    var dx = MathF.Abs(next.Position - prev.Position);
                    var dy = MathF.Abs(next.Value - prev.Value);
                    if (dy < 0.001f || (dx > 0.00001f && MathF.Atan2(dy, dx) * 180 / MathF.PI < 5))
                        continue;

                    var length = MathF.Sqrt(dx * dx + dy * dy);
                    var startBucket = (int)MathF.Floor(prev.Position / bucketSize);
                    var endBucket = (int)MathF.Floor(next.Position / bucketSize);

                    for (var bucket = startBucket; bucket < buckets.Length && bucket <= endBucket; bucket++)
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
                    AddStop(color, i * bucketSize / Duration);
                    if (i < buckets.Length - 1)
                        AddStop(color, (i + 1) * bucketSize / Duration);
                }
            }

            AddStop(Color.FromRgb(0, 0, 0), 1);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
