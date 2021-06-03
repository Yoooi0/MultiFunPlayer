using System;

namespace MultiFunPlayer.Common.Input
{
    public interface IInputProcessor : IDisposable
    {
        event EventHandler<IInputGesture> OnGesture;
    }
}