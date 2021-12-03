using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

public abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public float Value { get; protected set; }

    [JsonProperty] public float Speed { get; set; } = 1;
    [JsonProperty] public float Minimum { get; set; } = 0;
    [JsonProperty] public float Maximum { get; set; } = 100;

    public abstract void Update(float deltaTime);
}
