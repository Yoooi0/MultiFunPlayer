using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

public abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public float Value { get; protected set; }

    public event EventHandler SyncRequest;

    [JsonProperty] public float Speed { get; set; } = 1;
    [JsonProperty] public float Minimum { get; set; } = 0;
    [JsonProperty] public float Maximum { get; set; } = 100;

    protected AbstractMotionProvider()
    {
        PropertyChanged += (_, e) =>
        {
            if (ShouldRequestSyncOnPropertyChange(e.PropertyName))
                SyncRequest?.Invoke(this, null);
        };
    }

    protected virtual bool ShouldRequestSyncOnPropertyChange(string propertyName)
    {
        if (propertyName == nameof(Value))
            return false;

        return true;
    }

    public abstract void Update(float deltaTime);
}
