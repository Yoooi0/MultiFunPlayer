using System;

namespace MultiFunPlayer.Input
{
    public interface IInputGestureDescriptor : IEquatable<IInputGestureDescriptor>
    {
        bool IEquatable<IInputGestureDescriptor>.Equals(IInputGestureDescriptor other) => Equals(this, other);
    }

    public interface ISimpleInputGestureDescriptor : IInputGestureDescriptor { }
    public interface IAxisInputGestureDescriptor : IInputGestureDescriptor { }
}
