using Stylet;

namespace MultiFunPlayer.MotionProvider;

public abstract class AbstractMotionProvider : Screen, IMotionProvider
{
    public abstract string Name { get; }
    public float Value { get; protected set; }

    public abstract void Update();
}
