using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public interface IInputGestureDescriptor : IEquatable<IInputGestureDescriptor>
    {
        bool IEquatable<IInputGestureDescriptor>.Equals(IInputGestureDescriptor other) => object.Equals(this, other);
    }

    public interface IInputGesture : IEquatable<IInputGesture>
    {
        IInputGestureDescriptor Descriptor { get; }

        bool Equals(object other) => Equals(other as IInputGesture);
        bool IEquatable<IInputGesture>.Equals(IInputGesture other) => Descriptor.Equals(other.Descriptor);
    }

    public interface IAxisInputGesture : IInputGesture
    {
        float Value { get; }
        float Delta { get; }
    }
}
