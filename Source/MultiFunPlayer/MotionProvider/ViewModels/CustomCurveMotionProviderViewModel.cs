using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Custom Curve")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal class CustomCurveMotionProviderViewModel : AbstractMotionProvider
{
    private readonly object _stateLock = new();

    private int _index;
    private KeyframeCollection _keyframes;
    private bool _playing;
    private int _pendingRefreshFlag;

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableConcurrentCollection<Point> Points { get; set; }

    [JsonProperty] public InterpolationType InterpolationType { get; set; }
    [JsonProperty] public double Duration { get; set; } = 10;
    [JsonProperty] public bool IsLooping { get; set; } = true;

    [DependsOn(nameof(Duration))]
    public Rect Viewport => new(0, 0, Duration, 1);

    [DoNotNotify] public double Time { get; private set; }

    public CustomCurveMotionProviderViewModel(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator)
    {
        Points = new ObservableConcurrentCollection<Point> { new Point() };
        _pendingRefreshFlag = 1;

        ResetState(true);
    }

    protected override bool ShouldSyncOnPropertyChanged(string propertyName)
        => propertyName != nameof(Time);

    protected void OnPointsChanged(ObservableConcurrentCollection<Point> oldValue, ObservableConcurrentCollection<Point> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnPointsCollectionChanged;
        if (newValue != null)
            newValue.CollectionChanged += OnPointsCollectionChanged;

        Interlocked.Exchange(ref _pendingRefreshFlag, 1);
    }

    protected void OnViewportChanged()
        => Interlocked.Exchange(ref _pendingRefreshFlag, 1);

    protected void OnIsLoopingChanged()
    {
        lock (_stateLock)
            if (IsLooping)
                _playing = true;
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        => Interlocked.Exchange(ref _pendingRefreshFlag, 1);

    public override void Update(double deltaTime)
    {
        if (Points == null || Points.Count == 0)
            return;

        if (Interlocked.CompareExchange(ref _pendingRefreshFlag, 0, 1) == 1)
        {
            var newKeyframes = new KeyframeCollection(Points.Count + 2);

            var points = Points.Prepend(new Point(Viewport.Left, Points[0].Y))
                                .Append(new Point(Viewport.Right, Points[^1].Y));
            foreach (var point in points)
                newKeyframes.Add(point.X, point.Y);

            _keyframes = newKeyframes;

            ResetState(_playing);
        }

        if (_keyframes == null)
            return;

        lock (_stateLock)
        {
            if (!_playing)
                return;

            if (Time >= Duration || _index + 1 >= _keyframes.Count)
                ResetState(IsLooping);

            _index = _keyframes.AdvanceIndex(_index, Time);
            if (!_keyframes.ValidateIndex(_index) || !_keyframes.ValidateIndex(_index + 1))
                return;

            var newValue = MathUtils.Clamp01(_keyframes.Interpolate(_index, Time, InterpolationType));
            Value = MathUtils.Map(newValue, 0, 1, Minimum / 100, Maximum / 100);
            Time += Speed * deltaTime;
        }

        NotifyOfPropertyChange(nameof(Time));
    }

    public void Reset() => ResetState(true);
    private void ResetState(bool playing)
    {
        lock (_stateLock)
        {
            Time = 0;
            _index = -1;
            _playing = playing;
        }
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

        #region CustomCurveMotionProvider::IsLooping
        s.RegisterAction<DeviceAxis, bool>($"MotionProvider::{name}::IsLooping::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Enable looping"),
            (axis, enabled) => UpdateProperty(axis, p => p.IsLooping = enabled));

        s.RegisterAction<DeviceAxis>($"MotionProvider::{name}::IsLooping::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            axis => UpdateProperty(axis, p => p.IsLooping = !p.IsLooping));
        #endregion

        #region CustomCurveMotionProvider::Reset
        s.RegisterAction<DeviceAxis, bool>($"MotionProvider::{name}::Reset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Request sync").WithDefaultValue(true),
            (axis, sync) => UpdateProperty(axis, p => {
                if (sync)
                    p.RequestSync();
                p.ResetState(true);
            }));
        #endregion

        #region CustomCurveMotionProvider::Points
        s.RegisterAction<DeviceAxis, PointsActionSettingsViewModel>($"MotionProvider::{name}::Points::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(new PointsActionSettingsViewModel())
                  .WithTemplateName("CustomCurveMotionProviderPointsTemplate")
                  .WithCustomToString(x => $"Points({x.Points.Count})"),
            (axis, vm) => UpdateProperty(axis, p =>
            {
                p.Duration = vm.Duration;
                p.InterpolationType = vm.InterpolationType;
                p.Points.SetFrom(vm.Points);
            }));
        #endregion
    }

    [AddINotifyPropertyChangedInterface]
    private partial record PointsActionSettingsViewModel(ObservableConcurrentCollection<Point> Points, double Duration, InterpolationType InterpolationType)
    {
        public PointsActionSettingsViewModel()
            : this(new ObservableConcurrentCollection<Point>() { new Point(0.5, 0.5) }, 1, InterpolationType.Linear)
        { }

        [JsonIgnore]
        [DependsOn(nameof(Duration))]
        public Rect Viewport => new(0, 0, Duration, 1);
    }
}