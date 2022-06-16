using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;

namespace MultiFunPlayer.Input;

public class GestureEventArgs : EventArgs
{
    public GestureEventArgs(IInputGesture gesture) => Gesture = gesture;

    public bool Handled { get; set; }
    public IInputGesture Gesture { get; }
}

public interface IShortcutManager : IDisposable
{
    bool HandleGestures { get; set; }

    event EventHandler<GestureEventArgs> OnGesture;

    ObservableConcurrentCollection<IShortcutActionDescriptor> AvailableActions { get; }
    ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> Bindings { get; }

    void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action);

    void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure) => RegisterAction(name, configure, ShortcutActionDescriptorFlags.AcceptsSimpleGesture);
    void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure, ShortcutActionDescriptorFlags flags);
    void UnregisterAction(string name) => UnregisterAction(new ShortcutActionDescriptor(name, ShortcutActionDescriptorFlags.All));
    void UnregisterAction(IShortcutActionDescriptor actionDescriptor);

    void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
    void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
}

public class ShortcutManager : IShortcutManager
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<IShortcutActionDescriptor, IShortcutActionBuilder> _actionBuilders;
    private readonly List<IInputProcessor> _processors;

    public event EventHandler<GestureEventArgs> OnGesture;

    public ObservableConcurrentCollection<IShortcutActionDescriptor> AvailableActions { get; }
    public ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> Bindings { get; }

    public bool HandleGestures { get; set; } = true;

    public ShortcutManager(IEnumerable<IInputProcessor> processors)
    {
        AvailableActions = new ObservableConcurrentCollection<IShortcutActionDescriptor>();
        Bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>>();

        _actionBuilders = new Dictionary<IShortcutActionDescriptor, IShortcutActionBuilder>();
        _processors = processors.ToList();
        foreach (var processor in _processors)
            processor.OnGesture += HandleGesture;
    }

    public void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure, ShortcutActionDescriptorFlags flags)
    {
        var descriptor = new ShortcutActionDescriptor(name, flags);
        var builder = configure(new ShortcutBuilder(descriptor));

        AvailableActions.Add(descriptor);
        _actionBuilders.Add(descriptor, builder);
    }

    public void UnregisterAction(IShortcutActionDescriptor actionDescriptor)
    {
        AvailableActions.Remove(actionDescriptor);
        _actionBuilders.Remove(actionDescriptor);

        foreach (var (gesture, assignedActions) in Bindings)
            foreach (var action in assignedActions.Where(a => actionDescriptor.Equals(a.Descriptor)).ToList())
                assignedActions.Remove(action);
    }

    public void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        RegisterGesture(gestureDescriptor);

        var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
        var actions = Bindings[gestureDescriptor];
        actions.Add(action);
    }

    public void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        RegisterGesture(gestureDescriptor);

        var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
        PopulateShortcutInstanceWithSettings(action, values);

        var actions = Bindings[gestureDescriptor];
        actions.Add(action);
    }

    private void PopulateShortcutInstanceWithSettings(IShortcutAction action, IEnumerable<TypedValue> values)
    {
        foreach (var (actionSetting, value) in action.Settings.Zip(values))
        {
            var actionSettingType = actionSetting.GetType().GetGenericArguments()[0];
            var valueType = value.Type;

            if (actionSettingType != valueType)
            {
                Logger.Warn($"Action \"{action.Descriptor}\" setting type mismatch! [\"{actionSettingType}\" != \"{valueType}\"]");
                continue;
            }

            actionSetting.Value = value.Value;
        }
    }

    private IShortcutAction CreateShortcutInstanceFromDescriptor(IShortcutActionDescriptor actionDescriptor)
    {
        var builder = _actionBuilders[actionDescriptor];
        var action = builder.Build();
        return action;
    }

    public void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action)
    {
        if (gestureDescriptor == null || action == null)
            return;
        if (!Bindings.ContainsKey(gestureDescriptor))
            return;

        var actions = Bindings[gestureDescriptor];
        actions.Remove(action);
    }

    public void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (Bindings.ContainsKey(gestureDescriptor))
            return;

        Bindings.Add(gestureDescriptor, new ObservableConcurrentCollection<IShortcutAction>());
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
        if (!Bindings.TryGetValue(gesture.Descriptor, out var assignedActions))
            return;
        if (assignedActions.Count == 0)
            return;

        Logger.Trace($"Handling {gesture.Descriptor} gesture");
        foreach (var action in assignedActions)
        {
            Logger.Trace($"Invoking {action.Descriptor} action");
            action.Invoke(gesture);
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
