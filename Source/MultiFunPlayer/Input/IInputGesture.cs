namespace MultiFunPlayer.Input;

public interface IInputGesture
{
    IInputGestureDescriptor Descriptor { get; }
}

public interface ISimpleInputGesture : IInputGesture
{
    bool State { get; }
}

public interface IAxisInputGesture : IInputGesture
{
    public double Value { get; }
    public double Delta { get; }
    public double DeltaTime { get; }
}

public abstract class AbstractSimpleInputGesture(ISimpleInputGestureDescriptor descriptor, bool state) : ISimpleInputGesture
{
    public IInputGestureDescriptor Descriptor { get; } = descriptor;
    public bool State { get; } = state;

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

public interface IInputGestureData { }
public interface ISimpleInputGestureData : IInputGestureData { }
public interface IAxisInputGestureData : IInputGestureData
{
    double ValueOrDelta { get; }
    double DeltaTime { get; }
    public bool IsAbsolute { get; }
    public bool IsRelative => !IsAbsolute;

    public double ApplyTo(double value, double deltaModifier = 1);
}

public sealed class SimpleInputGestureData : ISimpleInputGestureData
{
    private static readonly SimpleInputGestureData _default = new();

    public static SimpleInputGestureData FromGesture(ISimpleInputGesture gesture) => _default;
    public static SimpleInputGestureData FromGesture(IAxisInputGesture gesture) => _default;
}

public sealed class AxisInputGestureData(double value, double deltaTime, bool isAbsolute) : IAxisInputGestureData
{
    public double ValueOrDelta { get; } = value;
    public double DeltaTime { get; } = deltaTime;
    public bool IsAbsolute { get; } = isAbsolute;

    public double ApplyTo(double value, double deltaModifier = 1)
        => IsAbsolute ? ValueOrDelta : value + ValueOrDelta * deltaModifier;

    public static AxisInputGestureData FromGestureAbsolute(IAxisInputGesture gesture, bool invertValue)
        => new(invertValue ? 1 - gesture.Value : gesture.Value, gesture.DeltaTime, true);

    public static AxisInputGestureData FromGestureRelative(IAxisInputGesture gesture, bool invertDelta)
        => new(gesture.Delta * (invertDelta ? -1 : 1), gesture.DeltaTime, false);
}