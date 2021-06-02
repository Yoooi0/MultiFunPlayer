using MultiFunPlayer.Common.Input.Gesture;
using System;
using System.Collections.Generic;

namespace MultiFunPlayer.Common.Input.Processor
{
    public interface IInputProcessor : IDisposable
    {
        event EventHandler<IInputGesture> OnGesture;
    }
}