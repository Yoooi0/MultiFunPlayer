namespace MultiFunPlayer.Input;

public interface IInputGestureData;
public interface ISimpleInputGestureData : IInputGestureData;
public interface IAxisInputGestureData : IInputGestureData
{
    double ValueOrDelta { get; }
    double DeltaTime { get; }
    public bool IsAbsolute { get; }
    public bool IsRelative => !IsAbsolute;

    public double ApplyTo(double value, double deltaModifier = 1);
}

internal sealed record SimpleInputGestureData : ISimpleInputGestureData
{
    public static readonly SimpleInputGestureData Default = new();
}

internal sealed record AxisInputGestureData : IAxisInputGestureData
{
    public double ValueOrDelta { get; }
    public double DeltaTime { get; }
    public bool IsAbsolute { get; }

    private AxisInputGestureData(double valueOrDelta, double deltaTime, bool isAbsolute)
    {
        ValueOrDelta = valueOrDelta;
        DeltaTime = deltaTime;
        IsAbsolute = isAbsolute;
    }

    public double ApplyTo(double value, double deltaModifier = 1)
        => IsAbsolute ? ValueOrDelta : value + ValueOrDelta * deltaModifier;

    public static AxisInputGestureData FromGestureAbsolute(IAxisInputGesture gesture, bool invertValue)
        => Absolute(invertValue ? 1 - gesture.Value : gesture.Value, gesture.DeltaTime);

    public static AxisInputGestureData FromGestureRelative(IAxisInputGesture gesture, bool invertDelta)
        => Relative(gesture.Delta * (invertDelta ? -1 : 1), gesture.DeltaTime);

    public static AxisInputGestureData Absolute(double value, double deltaTime)
        => new(value, deltaTime, isAbsolute: true);
    public static AxisInputGestureData Relative(double delta, double deltaTime)
        => new(delta, deltaTime, isAbsolute: false);
}