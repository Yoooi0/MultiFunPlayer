using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;

namespace MultiFunPlayer.Input;

public interface IShortcutBinder : IDisposable
{
    bool HandleGestures { get; set; }

    event EventHandler<GestureEventArgs> OnGesture;

    ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutActionConfiguration>> Bindings { get; }

    void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration action);

    void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
    void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
}

public class ShortcutBinder : IShortcutBinder
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

    public void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        if (configuration == null)
            return;

        RegisterGesture(gestureDescriptor);

        var configurations = Bindings[gestureDescriptor];
        configurations.Add(configuration);
    }

    public void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        var configuration = _shortcutManager.CreateShortcutActionConfigurationInstance(actionDescriptor);
        if (configuration == null)
            return;

        RegisterGesture(gestureDescriptor);
        PopulateShortcutInstanceWithSettings(configuration, values);

        var configurations = Bindings[gestureDescriptor];
        configurations.Add(configuration);
    }

    private void PopulateShortcutInstanceWithSettings(IShortcutActionConfiguration configuration, IEnumerable<TypedValue> values)
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
