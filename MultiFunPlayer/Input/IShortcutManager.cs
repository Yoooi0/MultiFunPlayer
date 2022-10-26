using MultiFunPlayer.Common;
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
    event EventHandler<IShortcutActionDescriptor> ActionRegistered;
    event EventHandler<IShortcutActionDescriptor> ActionUnregistered;

    ObservableConcurrentCollection<IShortcutActionDescriptor> AvailableActions { get; }

    void RegisterAction(string name, Action action);
    void RegisterAction<T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action);
    void RegisterAction<T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action);
    void RegisterAction<T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action);
    void RegisterAction<T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action);
    void RegisterAction<T0, T1, T2, T3, T4>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action);

    void RegisterAction<TG>(string name, Action<TG> action) where TG : IInputGesture;
    void RegisterAction<TG, T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TG, T0> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TG, T0, T1> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TG, T0, T1, T2> action) where TG : IInputGesture;
    void RegisterAction<TG, T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TG, T0, T1, T2, T3> action) where TG : IInputGesture;

    void UnregisterAction(IShortcutActionDescriptor descriptor);
    void UnregisterAction(string name) => UnregisterAction(new ShortcutActionDescriptor(name));

    bool ActionAcceptsGesture(IShortcutActionDescriptor actionDescriptor, IInputGestureDescriptor gestureDescriptor);
    IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(IShortcutActionDescriptor descriptor);

    void Invoke(IShortcutActionDescriptor descriptor, params object[] arguments);
    void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture);
    void Invoke(string name, params object[] arguments) => Invoke(new ShortcutActionDescriptor(name), arguments);
}

public class ShortcutManager : IShortcutManager
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<IShortcutActionDescriptor, IShortcutAction> _actions;
    private readonly Dictionary<IShortcutActionDescriptor, IShortcutActionConfigurationBuilder> _actionConfigurationBuilders;

    public event EventHandler<IShortcutActionDescriptor> ActionRegistered;
    public event EventHandler<IShortcutActionDescriptor> ActionUnregistered;

    public ObservableConcurrentCollection<IShortcutActionDescriptor> AvailableActions { get; }

    public ShortcutManager()
    {
        AvailableActions = new ObservableConcurrentCollection<IShortcutActionDescriptor>();

        _actions = new Dictionary<IShortcutActionDescriptor, IShortcutAction>();
        _actionConfigurationBuilders = new Dictionary<IShortcutActionDescriptor, IShortcutActionConfigurationBuilder>();
    }

    public void RegisterAction(string name, Action callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<T0> callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<T0>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<T0, T1> callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<T0, T1>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<T0, T1, T2> callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<T0, T1, T2>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<T0, T1, T2, T3> callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<T0, T1, T2, T3>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<T0, T1, T2, T3, T4>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> builder4, Action<T0, T1, T2, T3, T4> callback)
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<T0, T1, T2, T3, T4>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>()), builder4(new ShortcutSettingBuilder<T4>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<TG>(string name, Action<TG> callback) where TG : IInputGesture
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<TG>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<TG, T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<TG, T0> callback) where TG : IInputGesture
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<TG, T0>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<TG, T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<TG, T0, T1> callback) where TG : IInputGesture
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<TG, T0, T1>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        AvailableActions.Add(descriptor); 
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<TG, T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<TG, T0, T1, T2> callback) where TG : IInputGesture
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<TG, T0, T1, T2>(descriptor, callback)); 
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        AvailableActions.Add(descriptor); 
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void RegisterAction<TG, T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<TG, T0, T1, T2, T3> callback) where TG : IInputGesture
    {
        var descriptor = new ShortcutActionDescriptor(name);
        _actions.Add(descriptor, new ShortcutAction<TG, T0, T1, T2, T3>(descriptor, callback));
        _actionConfigurationBuilders.Add(descriptor, new ShortcutActionConfigurationBuilder(descriptor, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        AvailableActions.Add(descriptor);
        ActionRegistered?.Invoke(this, descriptor);
    }

    public void UnregisterAction(IShortcutActionDescriptor descriptor)
    {
        if (!_actions.ContainsKey(descriptor))
            return;

        _actions.Remove(descriptor);
        _actionConfigurationBuilders.Remove(descriptor);

        AvailableActions.Remove(descriptor);
        ActionUnregistered?.Invoke(this, descriptor);
    }

    public bool ActionAcceptsGesture(IShortcutActionDescriptor actionDescription, IInputGestureDescriptor gestureDescriptor)
    {
        if (!_actions.TryGetValue(actionDescription, out var action))
            return false;

        var genericArguments = action.GetType().GetGenericArguments();
        if (gestureDescriptor.GetType().IsAssignableTo(typeof(ISimpleInputGestureDescriptor)))
            return genericArguments == null || genericArguments.Length == 0 || !genericArguments[0].IsAssignableTo(typeof(IInputGesture)) || genericArguments[0] == typeof(ISimpleInputGesture);
        else if (gestureDescriptor.GetType().IsAssignableTo(typeof(IAxisInputGestureDescriptor)))
            return genericArguments != null && genericArguments.Length > 0 && genericArguments[0] == typeof(IAxisInputGesture);

        return false;
    }

    public IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(IShortcutActionDescriptor actionDescriptor)
    {
        if (!_actionConfigurationBuilders.TryGetValue(actionDescriptor, out var builder))
            return null;

        return builder.Build();
    }

    public void Invoke(IShortcutActionDescriptor descriptor, params object[] arguments)
    {
        if (!_actions.TryGetValue(descriptor, out var action))
            return;

        action.Invoke(arguments);
    }

    public void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGesture gesture)
    {
        if (!_actions.TryGetValue(actionConfiguration.Descriptor, out var action))
            return;

        var genericArguments = action.GetType().GetGenericArguments();
        if (genericArguments == null || genericArguments.Length == 0)
            action.Invoke();
        else if (genericArguments[0].IsAssignableTo(typeof(IInputGesture)))
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
