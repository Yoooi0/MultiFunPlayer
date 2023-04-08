using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Input;

internal interface IShortcutBinder : IDisposable
{
    bool HandleGestures { get; set; }

    event EventHandler<GestureEventArgs> OnGesture;

    IReadOnlyObservableConcurrentCollection<IShortcutBinding> Bindings { get; }

    IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<object> values);

    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<TypedValue> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<object> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);

    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration action);

    IShortcutBinding GetOrCreateBinding(IInputGestureDescriptor gestureDescriptor);
    IShortcutBinding GetBinding(IInputGestureDescriptor gestureDescriptor);
    void AddBinding(IShortcutBinding binding);
    bool RemoveBinding(IShortcutBinding binding);
    bool RemoveBinding(IInputGestureDescriptor gestureDescriptor);

    bool ContainsBinding(IInputGestureDescriptor gestureDescriptor);
    bool TryGetBinding(IInputGestureDescriptor gestureDescriptor, out IShortcutBinding binding);
    void Clear();
}

internal class ShortcutBinder : IShortcutBinder
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ObservableConcurrentCollection<IShortcutBinding> _bindings;
    private readonly ConcurrentDictionary<IInputGestureDescriptor, IShortcutBinding> _bindingLookup;
    private readonly IShortcutManager _shortcutManager;
    private readonly List<IInputProcessor> _processors;

    public event EventHandler<GestureEventArgs> OnGesture;

    public IReadOnlyObservableConcurrentCollection<IShortcutBinding> Bindings => _bindings;

    public bool HandleGestures { get; set; } = true;

    public ShortcutBinder(IShortcutManager shortcutManager, IEnumerable<IInputProcessor> processors)
    {
        _shortcutManager = shortcutManager;
        _bindings = new ObservableConcurrentCollection<IShortcutBinding>();
        _bindingLookup = new ConcurrentDictionary<IInputGestureDescriptor, IShortcutBinding>();

        _processors = processors.ToList();
        foreach (var processor in _processors)
            processor.OnGesture += HandleGesture;

        _shortcutManager.ActionUnregistered += OnActionUnregistered;
    }

    private void OnActionUnregistered(object sender, IShortcutActionDescriptor descriptor)
    {
        foreach (var binding in _bindings)
            foreach (var configuration in binding.Configurations.Where(a => descriptor.Equals(a.Descriptor)).ToList())
                binding.Configurations.Remove(configuration);
    }

    public IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        configuration.Populate(values);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<object> values)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        configuration.Populate(values);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    private void BindConfiguration(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration configuration)
    {
        if (configuration == null)
            return;

        var binding = GetOrCreateBinding(gestureDescriptor);
        var configurations = binding.Configurations;
        configurations.Add(configuration);
    }

    public void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration configuration)
    {
        if (gestureDescriptor == null || configuration == null)
            return;
        if (!_bindingLookup.ContainsKey(gestureDescriptor))
            return;

        var binding = _bindingLookup[gestureDescriptor];
        var configurations = binding.Configurations;
        configurations.Remove(configuration);
    }

    public IShortcutBinding GetOrCreateBinding(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return null;
        if (_bindingLookup.TryGetValue(gestureDescriptor, out var binding))
            return binding;

        binding = new ShortcutBinding(gestureDescriptor);
        AddBinding(binding);
        return binding;
    }

    public IShortcutBinding GetBinding(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return null;
        if (_bindingLookup.TryGetValue(gestureDescriptor, out var binding))
            return binding;
        return null;
    }

    public void AddBinding(IShortcutBinding binding)
    {
        if (_bindingLookup.ContainsKey(binding.Gesture))
            return;

        var invalidConfigurations = binding.Configurations.Where(c => !_shortcutManager.AvailableActions.Contains(c.Descriptor));
        foreach (var configuration in invalidConfigurations.ToList())
        {
            binding.Configurations.Remove(configuration);
            Logger.Warn($"Removed \"{configuration.Descriptor}\" missing action from \"{binding.Gesture}\" binding!");
        }

        _bindings.Add(binding);
        _bindingLookup.TryAdd(binding.Gesture, binding);
    }

    public bool RemoveBinding(IShortcutBinding binding)
        => _bindings.Remove(binding) && _bindingLookup.TryRemove(binding.Gesture, out var _);

    public bool RemoveBinding(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return false;
        if (!_bindingLookup.TryGetValue(gestureDescriptor, out var binding))
            return false;

        return RemoveBinding(binding);
    }

    public bool ContainsBinding(IInputGestureDescriptor gestureDescriptor) => _bindingLookup.ContainsKey(gestureDescriptor);
    public bool TryGetBinding(IInputGestureDescriptor gestureDescriptor, out IShortcutBinding binding) => _bindingLookup.TryGetValue(gestureDescriptor, out binding);
    public void Clear()
    {
        _bindings.Clear();
        _bindingLookup.Clear();
    }

    private void HandleGesture(object sender, IInputGesture gesture)
    {
        var eventArgs = new GestureEventArgs(gesture);
        OnGesture?.Invoke(this, eventArgs);

        if (eventArgs.Handled)
            return;
        if (!HandleGestures)
            return;
        if (!_bindingLookup.TryGetValue(gesture.Descriptor, out var binding))
            return;
        if (binding.Configurations.Count == 0)
            return;
        if (!binding.Enabled)
            return;

        Logger.Trace($"Handling {gesture.Descriptor} gesture");
        foreach (var configuration in binding.Configurations)
        {
            Logger.Trace($"Invoking {configuration.Descriptor} action");
            _shortcutManager.Invoke(configuration, gesture);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        foreach (var processor in _processors)
            processor.OnGesture -= HandleGesture;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
