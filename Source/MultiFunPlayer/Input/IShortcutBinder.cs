using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Input;

internal interface IShortcutBinder : IDisposable
{
    bool HandleGestures { get; set; }
    IReadOnlyObservableConcurrentCollection<IShortcutBinding> Bindings { get; }

    IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, string actionName);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<TypedValue> values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<object> values);

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
    private readonly IInputProcessorManager _inputManager;
    private readonly IShortcutManager _shortcutManager;

    public bool HandleGestures { get; set; } = true;

    public IReadOnlyObservableConcurrentCollection<IShortcutBinding> Bindings => _bindings;

    public ShortcutBinder(IInputProcessorManager inputManager, IShortcutManager shortcutManager)
    {
        _inputManager = inputManager;
        _shortcutManager = shortcutManager;
        _bindings = [];
        _bindingLookup = new ConcurrentDictionary<IInputGestureDescriptor, IShortcutBinding>();

        _inputManager.OnGesture += HandleGesture;
        _shortcutManager.ActionUnregistered += OnActionUnregistered;
    }

    private void OnActionUnregistered(object sender, string actionName)
    {
        foreach (var binding in _bindings)
            foreach (var configuration in binding.Configurations.Where(a => actionName.Equals(a.Name)).ToList())
                binding.Configurations.Remove(configuration);
    }

    public IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, string actionName)
    {
        if (gestureDescriptor == null || actionName == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionName);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || actionName == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionName);
        configuration.Populate(values);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<object> values)
    {
        if (gestureDescriptor == null || actionName == null)
            return null;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionName);
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
        if (!_bindingLookup.TryGetValue(gestureDescriptor, out var binding))
            return;

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

        var invalidConfigurations = binding.Configurations.Where(c => !_shortcutManager.AvailableActions.Contains(c.Name));
        foreach (var configuration in invalidConfigurations.ToList())
        {
            binding.Configurations.Remove(configuration);
            Logger.Warn($"Removed \"{configuration.Name}\" missing action from \"{binding.Gesture}\" binding!");
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
        if (!HandleGestures)
            return;
        if (!_bindingLookup.TryGetValue(gesture.Descriptor, out var binding))
            return;
        if (binding.Configurations.Count == 0)
            return;
        if (!binding.Enabled)
            return;

        Logger.Trace("Handling {0} gesture", gesture.Descriptor);
        foreach (var configuration in binding.Configurations)
        {
            Logger.Trace("Invoking {0} action", configuration.Name);
            _shortcutManager.Invoke(configuration, gesture);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        _inputManager.OnGesture -= HandleGesture;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
