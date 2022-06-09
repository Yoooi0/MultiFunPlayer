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
    public float Value { get; protected set; }

    [JsonProperty] public float Speed { get; set; } = 1;
    [JsonProperty] public float Minimum { get; set; } = 0;
    [JsonProperty] public float Maximum { get; set; } = 100;

    protected AbstractMotionProvider(DeviceAxis target, IEventAggregator eventAggregator)
    {
        _target = target;
        _eventAggregator = eventAggregator;
    }

    protected void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (ShouldRequestSyncOnPropertyChange(e.PropertyName))
            _eventAggregator.Publish(new SyncRequestMessage(_target));
    }

    protected virtual bool ShouldRequestSyncOnPropertyChange(string propertyName) => propertyName != nameof(Value);

    public abstract void Update(float deltaTime);
}
