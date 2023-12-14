namespace MultiFunPlayer.Input;

public interface IInputGesture
{
    IInputGestureDescriptor Descriptor { get; }
}

public interface ISimpleInputGesture : IInputGesture { }
public interface IAxisInputGesture : IInputGesture
{
    double Value { get; }
    double Delta { get; }
    double DeltaTime { get; }
}

public abstract class AbstractSimpleInputGesture(ISimpleInputGestureDescriptor descriptor) : ISimpleInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;

    public override bool Equals(object obj) => obj is ISimpleInputGesture gesture && Descriptor.Equals(gesture.Descriptor);
    public override int GetHashCode() => HashCode.Combine(Descriptor);
}

public abstract class AbstractAxisInputGesture(IAxisInputGestureDescriptor descriptor, double value, double delta, double deltaTime) : IAxisInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;
    public double Value { get; } = value;
    public double Delta { get; } = delta;
    public double DeltaTime { get; } = deltaTime;

    public override bool Equals(object obj) => obj is IAxisInputGesture gesture && Descriptor.Equals(gesture.Descriptor);
    public override int GetHashCode() => HashCode.Combine(Descriptor);
}