namespace MultiFunPlayer.Input.TCode;

public record TCodeAxisGestureDescriptor(string Axis) : IAxisInputGestureDescriptor
{
    public override string ToString() => $"[TCode Axis: {Axis}]";
}

public class TCodeAxisGesture(TCodeAxisGestureDescriptor descriptor, double value, double delta, double deltaTime) : IAxisInputGesture
{
    public double Value { get; } = value;
    public double Delta { get; } = delta;
    public double DeltaTime { get; } = deltaTime;

    public string Axis => descriptor.Axis;
    public IInputGestureDescriptor Descriptor => descriptor;

    public override string ToString() => $"[TCode Axis: {Axis}, Value: {Value}, Delta: {Delta}]";

    public static TCodeAxisGesture Create(string axis, double value, double delta, double deltaTime) => new(new(axis), value, delta, deltaTime);
}
