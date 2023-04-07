using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Input;

internal interface IShortcutBinder : IDisposable
{
    bool HandleGestures { get; set; }

    event EventHandler<GestureEventArgs> OnGesture;

    IReadOnlyConcurrentObservableCollection<IShortcutBinding> Bindings { get; }

    IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<object> values);

    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<TypedValue> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<object> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);

    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration action);

    IShortcutBinding GetOrCreateBinding(IInputGestureDescriptor gestureDescriptor);
    void RemoveBinding(IShortcutBinding binding);
    void RemoveBinding(IInputGestureDescriptor gestureDescriptor);

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

    public IReadOnlyConcurrentObservableCollection<IShortcutBinding> Bindings => _bindings;

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
        if (gestureDescriptor == null || !CreateShortcutActionConfigurationInstance(actionDescriptor, out var configuration))
            return null;

        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || !CreateShortcutActionConfigurationInstance(actionDescriptor, out var configuration))
            return null;

        PopulateShortcutConfigurationWithSettings(configuration, values);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<object> values)
    {
        if (gestureDescriptor == null || !CreateShortcutActionConfigurationInstance(actionDescriptor, out var configuration))
            return null;

        PopulateShortcutConfigurationWithSettings(configuration, values);
        BindConfiguration(gestureDescriptor, configuration);
        return configuration;
    }

    private bool CreateShortcutActionConfigurationInstance(IShortcutActionDescriptor actionDescriptor, out IShortcutActionConfiguration configuration)
    {
        configuration = null;
        if (actionDescriptor == null)
            return false;

        configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        return configuration != null;
    }

    private void BindConfiguration(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration configuration)
    {
        if (configuration == null)
            return;

        var binding = GetOrCreateBinding(gestureDescriptor);
        var configurations = binding.Configurations;
        configurations.Add(configuration);
    }

    private void PopulateShortcutConfigurationWithSettings(IShortcutActionConfiguration configuration, IEnumerable<TypedValue> values)
    {
        foreach (var (setting, value) in configuration.Settings.Zip(values))
        {
            var settingType = setting.GetType().GetGenericArguments()[0];
            var valueType = value.Type;

            if (settingType != valueType)
            {
                Logger.Warn($"Action \"{configuration.Descriptor}\" setting type mismatch! [\"{settingType}\" != \"{valueType}\"]");
                continue;
            }

            setting.Value = value.Value;
        }
    }

    private void PopulateShortcutConfigurationWithSettings(IShortcutActionConfiguration configuration, IEnumerable<object> values)
    {
        foreach (var (setting, value) in configuration.Settings.Zip(values))
        {
            var settingType = setting.GetType().GetGenericArguments()[0];
            var typeMatches = value == null ? !settingType.IsValueType || Nullable.GetUnderlyingType(settingType) != null : value.GetType() == settingType;

            if (!typeMatches)
            {
                Logger.Warn($"Action \"{configuration.Descriptor}\" setting type mismatch! [\"{settingType}\" != \"{value?.GetType()}\"]");
                continue;
            }

            setting.Value = value;
        }
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
        _bindings.Add(binding);
        _bindingLookup.TryAdd(gestureDescriptor, binding);
        return binding;
    }

    public void RemoveBinding(IShortcutBinding binding) => RemoveBinding(binding?.Gesture);
    public void RemoveBinding(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (!_bindingLookup.TryGetValue(gestureDescriptor, out var binding))
            return;

        _bindings.Remove(binding);
        _bindingLookup.TryRemove(gestureDescriptor, out var _);
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
