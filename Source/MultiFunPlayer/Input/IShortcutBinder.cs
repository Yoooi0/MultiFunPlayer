using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;

namespace MultiFunPlayer.Input;

internal interface IShortcutBinder : IDisposable
{
    bool HandleGestures { get; set; }

    event EventHandler<GestureEventArgs> OnGesture;

    ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutActionConfiguration>> Bindings { get; }

    IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<object> values);

    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<TypedValue> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);
    IShortcutActionConfiguration BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, string actionName, IEnumerable<object> values)
        => BindActionWithSettings(gestureDescriptor, new ShortcutActionDescriptor(actionName), values);

    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration action);

    void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
    void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
}

internal class ShortcutBinder : IShortcutBinder
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IShortcutManager _shortcutManager;
    private readonly List<IInputProcessor> _processors;

    public event EventHandler<GestureEventArgs> OnGesture;

    public ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutActionConfiguration>> Bindings { get; }

    public bool HandleGestures { get; set; } = true;

    public ShortcutBinder(IShortcutManager shortcutManager, IEnumerable<IInputProcessor> processors)
    {
        _shortcutManager = shortcutManager;
        Bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutActionConfiguration>>();

        _processors = processors.ToList();
        foreach (var processor in _processors)
            processor.OnGesture += HandleGesture;

        _shortcutManager.ActionUnregistered += OnActionUnregistered;
    }

    private void OnActionUnregistered(object sender, IShortcutActionDescriptor descriptor)
    {
        foreach (var (gesture, assignedConfigurations) in Bindings)
            foreach (var configuration in assignedConfigurations.Where(a => descriptor.Equals(a.Descriptor)).ToList())
                assignedConfigurations.Remove(configuration);
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

        RegisterGesture(gestureDescriptor);
        var configurations = Bindings[gestureDescriptor];
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
        if (!Bindings.ContainsKey(gestureDescriptor))
            return;

        var configurations = Bindings[gestureDescriptor];
        configurations.Remove(configuration);
    }

    public void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (Bindings.ContainsKey(gestureDescriptor))
            return;

        Bindings.Add(gestureDescriptor, new ObservableConcurrentCollection<IShortcutActionConfiguration>());
    }

    public void UnregisterGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (!Bindings.ContainsKey(gestureDescriptor))
            return;

        Bindings.Remove(gestureDescriptor);
    }

    private void HandleGesture(object sender, IInputGesture gesture)
    {
        var eventArgs = new GestureEventArgs(gesture);
        OnGesture?.Invoke(this, eventArgs);

        if (eventArgs.Handled)
            return;
        if (!HandleGestures)
            return;
        if (!Bindings.TryGetValue(gesture.Descriptor, out var assignedConfigurations))
            return;
        if (assignedConfigurations.Count == 0)
            return;

        Logger.Trace($"Handling {gesture.Descriptor} gesture");
        foreach (var configuration in assignedConfigurations)
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
