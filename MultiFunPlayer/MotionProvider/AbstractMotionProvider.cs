using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

public abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    private readonly DeviceAxis _target;
    private readonly IEventAggregator _eventAggregator;

    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public double Value { get; protected set; }

    [JsonProperty] public double Speed { get; set; } = 1;
    [JsonProperty] public double Minimum { get; set; } = 0;
    [JsonProperty] public double Maximum { get; set; } = 100;

    protected AbstractMotionProvider(DeviceAxis target, IEventAggregator eventAggregator)
    {
        _target = target;
        _eventAggregator = eventAggregator;
    }

    protected override void OnPropertyChanged(string propertyName)
    {
        if (propertyName != nameof(Value))
        {
            _eventAggregator?.Publish(new SyncRequestMessage(_target));
            base.OnPropertyChanged(propertyName);
        }
    }

    public abstract void Update(double deltaTime);
}
