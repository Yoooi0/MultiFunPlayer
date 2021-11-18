using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

public abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public float Value { get; protected set; }

    public abstract void Update(float deltaTime);
}
