using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public interface IInputGesture : IEquatable<IInputGesture> { }
    public interface IAxisInputGesture : IInputGesture
    {
        float Value { get; }
        float Delta { get; }
    }
}
