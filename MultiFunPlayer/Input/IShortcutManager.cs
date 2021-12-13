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

    IReadOnlyCollection<IShortcutActionDescriptor> Actions { get; }
    IReadOnlyDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> Bindings { get; }

    void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
    void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values);
    void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action);

    void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure) => RegisterAction(name, configure, ShortcutActionDescriptorFlags.AcceptsSimpleGesture);
    void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure, ShortcutActionDescriptorFlags flags);

    void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
    void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
}

public class ShortcutManager : IShortcutManager
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<IShortcutActionDescriptor, IShortcutActionBuilder> _actions;
    private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> _bindings;
    private readonly List<IInputProcessor> _processors;

    public event EventHandler<GestureEventArgs> OnGesture;

    public IReadOnlyCollection<IShortcutActionDescriptor> Actions => _actions.Keys;
    public IReadOnlyDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> Bindings => _bindings;

    public bool HandleGestures { get; set; } = true;

    public ShortcutManager(IEnumerable<IInputProcessor> processors)
    {
        _actions = new Dictionary<IShortcutActionDescriptor, IShortcutActionBuilder>();
        _bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>>();

        _processors = processors.ToList();
        foreach (var processor in _processors)
            processor.OnGesture += HandleGesture;
    }

    public void RegisterAction(string name, Func<INoSettingsShortcutActionBuilder, IShortcutActionBuilder> configure, ShortcutActionDescriptorFlags flags)
    {
        var descriptor = new ShortcutActionDescriptor(name, flags);
        var builder = configure(new ShortcutBuilder(descriptor));
        _actions.Add(descriptor, builder);
    }

    public void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        RegisterGesture(gestureDescriptor);

        var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
        var actions = _bindings[gestureDescriptor];
        actions.Add(action);
    }

    public void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<TypedValue> values)
    {
        if (gestureDescriptor == null || actionDescriptor == null)
            return;

        RegisterGesture(gestureDescriptor);

        var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
        PopulateShortcutInstanceWithSettings(action, values);

        var actions = _bindings[gestureDescriptor];
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
        var builder = _actions[actionDescriptor];
        var action = builder.Build();
        return action;
    }

    public void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action)
    {
        if (gestureDescriptor == null || action == null)
            return;
        if (!_bindings.ContainsKey(gestureDescriptor))
            return;

        var actions = _bindings[gestureDescriptor];
        actions.Remove(action);
    }

    public void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (_bindings.ContainsKey(gestureDescriptor))
            return;

        _bindings.Add(gestureDescriptor, new ObservableConcurrentCollection<IShortcutAction>());
    }

    public void UnregisterGesture(IInputGestureDescriptor gestureDescriptor)
    {
        if (gestureDescriptor == null)
            return;
        if (!_bindings.ContainsKey(gestureDescriptor))
            return;

        _bindings.Remove(gestureDescriptor);
    }

    private void HandleGesture(object sender, IInputGesture gesture)
    {
        var eventArgs = new GestureEventArgs(gesture);
        OnGesture?.Invoke(this, eventArgs);

        if (eventArgs.Handled)
            return;
        if (!HandleGestures)
            return;
        if (!_bindings.TryGetValue(gesture.Descriptor, out var actions))
            return;
        if (actions.Count == 0)
            return;

        Logger.Trace($"Handling {gesture.Descriptor} gesture");
        foreach (var action in actions)
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
