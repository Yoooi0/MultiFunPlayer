using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

internal abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    private readonly DeviceAxis _target;
    private readonly IEventAggregator _eventAggregator;

    public string Name { get; init; }
    [DoNotNotify] public double Value { get; protected set; }

    [JsonProperty] public double Speed { get; set; } = 1;
    [JsonProperty] public double Minimum { get; set; } = 0;
    [JsonProperty] public double Maximum { get; set; } = 1;

    protected AbstractMotionProvider(DeviceAxis target, IEventAggregator eventAggregator)
    {
        _target = target;
        _eventAggregator = eventAggregator;

        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    }

    protected override void OnPropertyChanged(string propertyName)
    {
        if (ShouldSyncOnPropertyChanged(propertyName))
            RequestSync();

        base.OnPropertyChanged(propertyName);
    }

    protected virtual bool ShouldSyncOnPropertyChanged(string propertyName) => true;
    protected void RequestSync() => _eventAggregator?.Publish(new SyncRequestMessage(_target));

    public abstract void Update(double deltaTime);

    protected static void RegisterActions<T>(IShortcutManager s, Func<DeviceAxis, T> getInstance) where T : AbstractMotionProvider
    {
        void UpdateProperty(DeviceAxis axis, Action<T> callback)
        {
            var motionProvider = getInstance(axis);
            if (motionProvider != null)
                callback(motionProvider);
        }

        var name = typeof(T).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        #region MotionProvider::Speed
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Speed::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").AsNumericUpDown(interval: 0.01, stringFormat: "{0:P0}"),
            (axis, offset) => UpdateProperty(axis, p => p.Speed = Math.Max(0.01, p.Speed + offset)));

        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Speed::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").AsNumericUpDown(minimum: 0.01, interval: 0.01, stringFormat: "{0:P0}"),
            (axis, value) => UpdateProperty(axis, p => p.Speed = Math.Max(0.01, value)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis>($"MotionProvider::{name}::Speed::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (data, axis) => UpdateProperty(axis, p => p.Speed = Math.Max(0.01, data.ApplyTo(p.Speed))));
        #endregion

        #region MotionProvider::Minimum
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Minimum::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").AsNumericUpDown(-1, 1, 0.01, "{0:P0}"),
            (axis, offset) => UpdateProperty(axis, p => p.Minimum = MathUtils.Clamp01(p.Minimum + offset)));

        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Minimum::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").AsNumericUpDown(0, 1, 0.01, "{0:P0}"),
            (axis, value) => UpdateProperty(axis, p => p.Minimum = MathUtils.Clamp01(value)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis>($"MotionProvider::{name}::Minimum::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (data, axis) => UpdateProperty(axis, p => p.Minimum = MathUtils.Clamp01(data.ApplyTo(p.Minimum))));
        #endregion

        #region MotionProvider::Maximum
        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Maximum::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").AsNumericUpDown(-1, 1, 0.01, "{0:P0}"),
            (axis, offset) => UpdateProperty(axis, p => p.Maximum = MathUtils.Clamp01(p.Maximum + offset)));

        s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Maximum::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").AsNumericUpDown(0, 1, 0.01, "{0:P0}"),
            (axis, value) => UpdateProperty(axis, p => p.Maximum = MathUtils.Clamp01(value)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis>($"MotionProvider::{name}::Maximum::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (data, axis) => UpdateProperty(axis, p => p.Maximum = MathUtils.Clamp01(data.ApplyTo(p.Maximum))));
        #endregion
    }
}
