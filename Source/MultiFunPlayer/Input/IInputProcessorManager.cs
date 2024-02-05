using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.XInput;

namespace MultiFunPlayer.Input;

internal interface IInputProcessorManager : IDisposable
{
    event EventHandler<IInputGesture> OnGesture;

    void RegisterProcessor(IInputProcessor processor);
    void UnregisterProcessor(IInputProcessor processor);
    InputProcessorManagerRegistration RegisterProcessor<T>(out T instance) where T : IInputProcessor;
}

internal struct InputProcessorManagerRegistration : IDisposable
{
    private IInputProcessorManager _inputManager;
    private IInputProcessor _processor;

    public InputProcessorManagerRegistration(IInputProcessorManager inputManager, IInputProcessor processor)
    {
        _inputManager = inputManager;
        _processor = processor;
        inputManager.RegisterProcessor(processor);
    }

    public void Dispose()
    {
        _inputManager?.UnregisterProcessor(_processor);
        _inputManager = null;
        _processor = null;
    }
}

internal sealed class InputProcessorManager : IInputProcessorManager
{
    private readonly object _lock = new();
    private readonly List<IInputProcessor> _processors;
    private readonly IInputProcessorFactory _processorFactory;

    public event EventHandler<IInputGesture> OnGesture;

    public InputProcessorManager(IInputProcessorFactory processorFactory)
    {
        _processors = [];
        _processorFactory = processorFactory;

        RegisterProcessor(_processorFactory.GetInputProcessor<XInputProcessor>());
        RegisterProcessor(_processorFactory.GetInputProcessor<RawInputProcessor>());
    }

    public void RegisterProcessor(IInputProcessor processor)
    {
        lock (_lock)
        {
            _processors.Add(processor);
            processor.OnGesture += HandleGesture;
        }
    }

    public void UnregisterProcessor(IInputProcessor processor)
    {
        lock (_lock)
        {
            _processors.Remove(processor);
            processor.OnGesture -= HandleGesture;
        }
    }

    public InputProcessorManagerRegistration RegisterProcessor<T>(out T instance) where T : IInputProcessor
    {
        instance = _processorFactory.GetInputProcessor<T>();
        if (_processors.Contains(instance))
            throw new InvalidOperationException($"Tried to reregister {typeof(T).Name} instance");

        return new(this, instance);
    }

    private void HandleGesture(object sender, IInputGesture gesture) => OnGesture?.Invoke(this, gesture);

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            foreach (var processor in _processors)
                processor.OnGesture -= HandleGesture;
            _processors.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
