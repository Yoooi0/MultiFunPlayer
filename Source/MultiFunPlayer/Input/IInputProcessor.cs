namespace MultiFunPlayer.Input;

internal interface IInputProcessor : IDisposable
{
    event EventHandler<IInputGesture> OnGesture;
}
