namespace MultiFunPlayer.Input;

internal interface IInputProcessorManager : IDisposable
{
    event EventHandler<IInputGesture> OnGesture;

    void AddProcessor(IInputProcessor processor);
    void RemoveProcessor(IInputProcessor processor);

    IInputProcessorManagerRegistration Register<T>(out T instance) where T : IInputProcessor
    {
        instance = (T)Activator.CreateInstance(typeof(T));
        return new(this, instance);
    }
}

internal struct IInputProcessorManagerRegistration : IDisposable
{
    private IInputProcessorManager _inputManager;
    private IInputProcessor _processor;

    public IInputProcessorManagerRegistration(IInputProcessorManager inputManager, IInputProcessor processor)
    {
        _inputManager = inputManager;
        _processor = processor;
        inputManager.AddProcessor(processor);
    }

    public void Dispose()
    {
        _inputManager?.RemoveProcessor(_processor);
        _inputManager = null;
        _processor = null;
    }
}

internal class InputProcessorManager : IInputProcessorManager
{
    private readonly List<IInputProcessor> _processors;
    private readonly object _lock = new();

    public event EventHandler<IInputGesture> OnGesture;

    public InputProcessorManager(IEnumerable<IInputProcessor> processors)
    {
        _processors = new List<IInputProcessor>();
        foreach (var processor in processors)
            AddProcessor(processor);
    }

    public void AddProcessor(IInputProcessor processor)
    {
        lock (_lock)
        {
            _processors.Add(processor);
            processor.OnGesture += HandleGesture;
        }
    }

    public void RemoveProcessor(IInputProcessor processor)
    {
        lock (_lock)
        {
            _processors.Remove(processor);
            processor.OnGesture -= HandleGesture;
        }
    }

    private void HandleGesture(object sender, IInputGesture gesture) => OnGesture?.Invoke(this, gesture);

    protected virtual void Dispose(bool disposing)
    {
        lock (_lock)
        {
            foreach (var processor in _processors.ToList())
                RemoveProcessor(processor);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
