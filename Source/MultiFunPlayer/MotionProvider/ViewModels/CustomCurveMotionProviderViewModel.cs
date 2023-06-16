using MultiFunPlayer.Common;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
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

        Value = MathUtils.Clamp01(_keyframes.Interpolate(_index, _time, InterpolationType));
        _time += Speed * deltaTime;
    }
}
