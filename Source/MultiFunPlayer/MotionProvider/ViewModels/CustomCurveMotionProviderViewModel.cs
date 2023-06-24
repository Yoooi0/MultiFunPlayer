using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Custom Curve")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal class CustomCurveMotionProviderViewModel : AbstractMotionProvider
{
    private double _time;
    private int _index;
    private KeyframeCollection _keyframes;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableConcurrentCollection<Point> Points { get; set; }

    [JsonProperty] public InterpolationType InterpolationType { get; set; }
    [JsonProperty] public double Duration { get; set; } = 10;

    [DependsOn(nameof(Duration))]
    public Rect Viewport => new(0, 0, Duration, 1);

    public CustomCurveMotionProviderViewModel(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator)
    {
        Points = new ObservableConcurrentCollection<Point> { new Point() };
    }

    protected void OnPointsChanged(ObservableConcurrentCollection<Point> oldValue, ObservableConcurrentCollection<Point> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnPointsCollectionChanged;
        if (newValue != null)
            newValue.CollectionChanged += OnPointsCollectionChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Interlocked.Exchange(ref _keyframes, null);
    }

    public override void Update(double deltaTime)
    {
        if (Points.Count == 0)
            return;

        if (_keyframes == null)
        {
            var newKeyframes = new KeyframeCollection(Points.Count + 2);

            var points = Points.Prepend(new Point(Viewport.Left, Points[0].Y))
                               .Append(new Point(Viewport.Right, Points[^1].Y));
            foreach (var point in points)
                newKeyframes.Add(point.X, point.Y);

            Interlocked.Exchange(ref _keyframes, newKeyframes);
            _index = newKeyframes.SearchForIndexBefore(_time);
        }

        if (_time > Duration || _index + 1 >= _keyframes.Count)
        {
            _time = 0;
            _index = -1;
        }

        _index = _keyframes.AdvanceIndex(_index, _time);
        if (!_keyframes.ValidateIndex(_index) || !_keyframes.ValidateIndex(_index + 1))
            return;

        var newValue = MathUtils.Clamp01(_keyframes.Interpolate(_index, _time, InterpolationType));
        Value = MathUtils.Map(newValue, 0, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }

    public static void RegisterActions(IShortcutManager s, Func<DeviceAxis, CustomCurveMotionProviderViewModel> getInstance)
    {
        void UpdateProperty(DeviceAxis axis, Action<CustomCurveMotionProviderViewModel> callback)
        {
            var motionProvider = getInstance(axis);
            if (motionProvider != null)
                callback(motionProvider);
        }

        AbstractMotionProvider.RegisterActions(s, getInstance);
        var name = typeof(CustomCurveMotionProviderViewModel).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        #region CustomCurveMotionProvider::InterpolationType
        s.RegisterAction<DeviceAxis, InterpolationType>($"MotionProvider::{name}::InterpolationType::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Interpolation type").WithItemsSource(EnumUtils.GetValues<InterpolationType>()),
            (axis, interpolationType) => UpdateProperty(axis, p => p.InterpolationType = interpolationType));
        #endregion

        #region CustomCurveMotionProvider::Duration
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Duration::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Duration").WithStringFormat("{}{0}s"),
            (axis, duration) => UpdateProperty(axis, p => p.Duration = duration));
        #endregion

        #region CustomCurveMotionProvider::Points
        s.RegisterAction<DeviceAxis, PointsActionSettingsViewModel>($"MotionProvider::{name}::Points::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(new(new ObservableConcurrentCollection<Point>() { new Point(0.5, 0.5) }, 1, InterpolationType.Linear))
                  .WithTemplateName("CustomCurveMotionProviderPointsTemplate")
                  .WithCustomToString(x => $"Points({x.Points.Count})"),
            (axis, vm) => UpdateProperty(axis, p =>
            {
                p.Points.Clear();
                p.Duration = vm.Duration;
                p.InterpolationType = vm.InterpolationType;
                p.Points.AddRange(vm.Points);
            }));
        #endregion
    }

    [AddINotifyPropertyChangedInterface]
    private partial record PointsActionSettingsViewModel(ObservableConcurrentCollection<Point> Points, double Duration, InterpolationType InterpolationType)
    {
        [JsonIgnore]
        [DependsOn(nameof(Duration))]
        public Rect Viewport => new(0, 0, Duration, 1);
    }
}
