using MultiFunPlayer.Common;
using NLog;

namespace MultiFunPlayer.Input;

internal class GestureEventArgs : EventArgs
{
    public GestureEventArgs(IInputGesture gesture) => Gesture = gesture;
    public IInputGesture Gesture { get; }
}

internal interface IShortcutManager : IDisposable
{
    event EventHandler<string> ActionRegistered;
    event EventHandler<string> ActionUnregistered;

    IReadOnlyObservableConcurrentCollection<string> AvailableActions { get; }

    void RegisterAction(string actionName, Action action);
    void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action);
    void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action);
    void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action);
    void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action);
    void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action);

    void RegisterAction<TG>(string actionName, Action<TG> action) where TG : IInputGesture;
    void RegisterAction<TG, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TG, T0> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TG, T0, T1> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TG, T0, T1, T2> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TG, T0, T1, T2, T3> action) where TG : IInputGesture;

    void UnregisterAction(string actionName);

    bool ActionAcceptsGesture(string actionName, IInputGestureDescriptor gestureDescriptor);
    IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(string actionName);

    void Invoke(string actionName, params object[] arguments);
    void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture);
}

internal class ShortcutManager : IShortcutManager
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ObservableConcurrentCollection<string> _availableActions;
    private readonly Dictionary<string, IShortcutAction> _actions;
    private readonly Dictionary<string, IShortcutActionConfigurationBuilder> _actionConfigurationBuilders;

    public event EventHandler<string> ActionRegistered;
    public event EventHandler<string> ActionUnregistered;

    public IReadOnlyObservableConcurrentCollection<string> AvailableActions => _availableActions;

    public ShortcutManager()
    {
        _availableActions = new ObservableConcurrentCollection<string>();

        _actions = new Dictionary<string, IShortcutAction>();
        _actionConfigurationBuilders = new Dictionary<string, IShortcutActionConfigurationBuilder>();
    }

    public void RegisterAction(string actionName, Action callback)
    {
        _actions.Add(actionName, new ShortcutAction(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<T0> callback)
    {
        _actions.Add(actionName, new ShortcutAction<T0>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<T0, T1> callback)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<T0, T1, T2> callback)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<T0, T1, T2, T3> callback)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2, T3>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> builder4, Action<T0, T1, T2, T3, T4> callback)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2, T3, T4>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>()), builder4(new ShortcutSettingBuilder<T4>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<TG>(string actionName, Action<TG> callback) where TG : IInputGesture
    {
        _actions.Add(actionName, new ShortcutAction<TG>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<TG, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<TG, T0> callback) where TG : IInputGesture
    {
        _actions.Add(actionName, new ShortcutAction<TG, T0>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<TG, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<TG, T0, T1> callback) where TG : IInputGesture
    {
        _actions.Add(actionName, new ShortcutAction<TG, T0, T1>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<TG, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<TG, T0, T1, T2> callback) where TG : IInputGesture
    {
        _actions.Add(actionName, new ShortcutAction<TG, T0, T1, T2>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void RegisterAction<TG, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<TG, T0, T1, T2, T3> callback) where TG : IInputGesture
    {
        _actions.Add(actionName, new ShortcutAction<TG, T0, T1, T2, T3>(callback));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        _availableActions.Add(actionName);
        ActionRegistered?.Invoke(this, actionName);
    }

    public void UnregisterAction(string actionName)
    {
        if (!_actions.ContainsKey(actionName))
            return;

        _actions.Remove(actionName);
        _actionConfigurationBuilders.Remove(actionName);

        _availableActions.Remove(actionName);
        ActionUnregistered?.Invoke(this, actionName);
    }

    public bool ActionAcceptsGesture(string actionName, IInputGestureDescriptor gestureDescriptor)
    {
        if (!_actions.TryGetValue(actionName, out var action))
            return false;

        if (gestureDescriptor is ISimpleInputGestureDescriptor)
            return action.Arguments.Count == 0 || !action.Arguments[0].IsAssignableTo(typeof(IInputGesture)) || typeof(ISimpleInputGesture).IsAssignableTo(action.Arguments[0]);
        else if (gestureDescriptor is IAxisInputGestureDescriptor)
            return action.Arguments.Count > 0 && typeof(IAxisInputGesture).IsAssignableTo(action.Arguments[0]);

        return false;
    }

    public IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(string actionName)
    {
        if (!_actionConfigurationBuilders.TryGetValue(actionName, out var builder))
            return null;

        return builder.Build();
    }

    public void Invoke(string actionName, params object[] arguments)
    {
        if (!_actions.TryGetValue(actionName, out var action))
            return;

        action.Invoke(arguments);
    }

    public void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture)
    {
        if (!_actions.TryGetValue(actionConfiguration.Name, out var action))
            return;

        if (action.Arguments.Count == 0)
            action.Invoke();
        else if (action.Arguments[0].IsAssignableTo(typeof(IInputGesture)))
            action.Invoke(actionConfiguration.GetActionParamsWithGesture(gesture));
        else
            action.Invoke(actionConfiguration.GetActionParams());
    }

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
