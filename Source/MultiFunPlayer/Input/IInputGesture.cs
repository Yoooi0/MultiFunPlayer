namespace MultiFunPlayer.Input;

public interface IInputGesture : IEquatable<IInputGesture>
{
    IInputGestureDescriptor Descriptor { get; }

    bool Equals(object other) => Equals(other as IInputGesture);
    bool IEquatable<IInputGesture>.Equals(IInputGesture other) => Descriptor.Equals(other.Descriptor);
}

public interface ISimpleInputGesture : IInputGesture { }
public interface IAxisInputGesture : IInputGesture
{
    double Value { get; }
    double Delta { get; }
    double DeltaTime { get; }
}
