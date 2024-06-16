using Stylet;

namespace MultiFunPlayer.Input;

internal interface IInputProcessor : IDisposable
{
    public static string EventAggregatorChannelName { get; } = "InputProcessor";
}

internal abstract class AbstractInputProcessor(IEventAggregator eventAggregator) : IInputProcessor
{
    protected virtual void PublishGesture(IInputGesture gesture)
        => eventAggregator.Publish(gesture, IInputProcessor.EventAggregatorChannelName);

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}