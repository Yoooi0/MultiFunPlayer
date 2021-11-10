namespace MultiFunPlayer.Input;

public interface IInputProcessor : IDisposable
{
    event EventHandler<IInputGesture> OnGesture;
}
