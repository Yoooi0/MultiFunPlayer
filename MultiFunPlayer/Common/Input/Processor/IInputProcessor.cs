using MultiFunPlayer.Common.Input.Gesture;
using System;

namespace MultiFunPlayer.Common.Input.Processor
{
    public interface IInputProcessor : IDisposable
    {
        event EventHandler<IInputGesture> OnGesture;
    }
}