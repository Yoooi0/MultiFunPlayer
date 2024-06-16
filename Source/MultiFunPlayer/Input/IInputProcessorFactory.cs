using StyletIoC;

namespace MultiFunPlayer.Input;

internal interface IInputProcessorFactory
{
    T GetInputProcessor<T>() where T : IInputProcessor;
}

internal sealed class InputProcessorFactory(IContainer container) : IInputProcessorFactory
{
    public T GetInputProcessor<T>() where T : IInputProcessor
        => container.Get<T>();
}