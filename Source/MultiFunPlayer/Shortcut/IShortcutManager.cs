using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Settings;
using NLog;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutManager : IDisposable
{
    bool HandleGestures { get; set; }
    IReadOnlyObservableConcurrentCollection<string> AvailableActions { get; }
    IReadOnlyObservableConcurrentCollection<IShortcut> Shortcuts { get; }

    IShortcutActionConfiguration BindAction(IShortcut shortcut, string actionName);
    IShortcutActionConfiguration BindActionWithSettings(IShortcut shortcut, string actionName, IEnumerable<TypedValue> values);
    IShortcutActionConfiguration BindActionWithSettings(IShortcut shortcut, string actionName, IEnumerable<object> values);

    void UnbindAction(IShortcut shortcut, IShortcutActionConfiguration action);

    IShortcut AddShortcut(IShortcut shortcut);
    IShortcut AddShortcut<T>(IInputGestureDescriptor gesture) where T : IShortcut
        => AddShortcut((IShortcut)Activator.CreateInstance(typeof(T), [gesture]));

    bool RemoveShortcut(IShortcut shortcut);
    void ClearShortcuts();

    void RegisterAction(string actionName, Action action);
    void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action);
    void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action);
    void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action);
    void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action);
    void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action);

    void RegisterAction<TD>(string actionName, Action<TD> action) where TD : IInputGestureData;
    void RegisterAction<TD, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TD, T0> action) where TD : IInputGestureData;
    void RegisterAction<TD, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TD, T0, T1> action) where TD : IInputGestureData;
    void RegisterAction<TD, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TD, T0, T1, T2> action) where TD : IInputGestureData;
    void RegisterAction<TD, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TD, T0, T1, T2, T3> action) where TD : IInputGestureData;

    void UnregisterAction(string actionName);

    bool ActionAcceptsGestureData(string actionName, Type gestureDataType);
    IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(string actionName);

    void Invoke(string actionName, params object[] arguments);
    void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData);
    void Invoke(string actionName);
    void Invoke<T0>(string actionName, T0 arg0);
    void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1);
    void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2);
    void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
    void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
}

internal sealed class ShortcutManager : IShortcutManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IInputProcessorManager _inputManager;
    private readonly ObservableConcurrentCollection<string> _availableActions;
    private readonly Dictionary<string, IShortcutAction> _actions;
    private readonly Dictionary<string, IShortcutActionConfigurationBuilder> _actionConfigurationBuilders;
    private readonly ObservableConcurrentCollection<IShortcut> _shortcuts;

    public bool HandleGestures { get; set; }
    public IReadOnlyObservableConcurrentCollection<string> AvailableActions => _availableActions;
    public IReadOnlyObservableConcurrentCollection<IShortcut> Shortcuts => _shortcuts;

    public ShortcutManager(IInputProcessorManager inputManager)
    {
        _availableActions = [];
        _actions = [];
        _actionConfigurationBuilders = [];
        _shortcuts = [];

        _inputManager = inputManager;
        _inputManager.OnGesture += HandleGesture;
    }

    public IShortcutActionConfiguration BindAction(IShortcut shortcut, string actionName)
    {
        if (shortcut == null || actionName == null)
            return null;

        var configuration = CreateShortcutActionConfigurationInstance(actionName);
        shortcut.Configurations.Add(configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IShortcut shortcut, string actionName, IEnumerable<TypedValue> values)
    {
        if (shortcut == null || actionName == null)
            return null;

        var configuration = CreateShortcutActionConfigurationInstance(actionName);
        configuration.Populate(values);
        shortcut.Configurations.Add(configuration);
        return configuration;
    }

    public IShortcutActionConfiguration BindActionWithSettings(IShortcut shortcut, string actionName, IEnumerable<object> values)
    {
        if (shortcut == null || actionName == null)
            return null;

        var configuration = CreateShortcutActionConfigurationInstance(actionName);
        configuration.Populate(values);
        shortcut.Configurations.Add(configuration);
        return configuration;
    }

    public void UnbindAction(IShortcut shortcut, IShortcutActionConfiguration configuration)
    {
        if (shortcut == null || configuration == null)
            return;

        var configurations = shortcut.Configurations;
        configurations.Remove(configuration);
    }

    public IShortcut AddShortcut(IShortcut shortcut)
    {
        var invalidConfigurations = shortcut.Configurations.Where(c => !AvailableActions.Contains(c.Name));
        foreach (var configuration in invalidConfigurations.ToList())
        {
            shortcut.Configurations.Remove(configuration);
            Logger.Warn($"Removed \"{configuration.Name}\" missing action from \"{shortcut.Gesture}\" shortcut!");
        }

        _shortcuts.Add(shortcut);
        return shortcut;
    }

    public bool RemoveShortcut(IShortcut shortcut)
        => _shortcuts.Remove(shortcut);
    public void ClearShortcuts()
        => _shortcuts.Clear();

    public void RegisterAction(string actionName, Action action)
    {
        _actions.Add(actionName, new ShortcutAction(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<T0> action)
    {
        _actions.Add(actionName, new ShortcutAction<T0>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<T0, T1> action)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<T0, T1, T2> action)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<T0, T1, T2, T3> action)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2, T3>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> builder4, Action<T0, T1, T2, T3, T4> action)
    {
        _actions.Add(actionName, new ShortcutAction<T0, T1, T2, T3, T4>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>()), builder4(new ShortcutSettingBuilder<T4>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<TD>(string actionName, Action<TD> action) where TD : IInputGestureData
    {
        _actions.Add(actionName, new ShortcutAction<TD>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<TD, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Action<TD, T0> action) where TD : IInputGestureData
    {
        _actions.Add(actionName, new ShortcutAction<TD, T0>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<TD, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Action<TD, T0, T1> action) where TD : IInputGestureData
    {
        _actions.Add(actionName, new ShortcutAction<TD, T0, T1>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<TD, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Action<TD, T0, T1, T2> action) where TD : IInputGestureData
    {
        _actions.Add(actionName, new ShortcutAction<TD, T0, T1, T2>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void RegisterAction<TD, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> builder0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> builder1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> builder2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> builder3, Action<TD, T0, T1, T2, T3> action) where TD : IInputGestureData
    {
        _actions.Add(actionName, new ShortcutAction<TD, T0, T1, T2, T3>(action));
        _actionConfigurationBuilders.Add(actionName, new ShortcutActionConfigurationBuilder(actionName, builder0(new ShortcutSettingBuilder<T0>()), builder1(new ShortcutSettingBuilder<T1>()), builder2(new ShortcutSettingBuilder<T2>()), builder3(new ShortcutSettingBuilder<T3>())));

        _availableActions.Add(actionName);
        Logger.Trace("Registered \"{0}\" action", actionName);
    }

    public void UnregisterAction(string actionName)
    {
        if (!_actions.ContainsKey(actionName))
            return;

        _actions.Remove(actionName);
        _actionConfigurationBuilders.Remove(actionName);

        _availableActions.Remove(actionName);

        foreach (var shortcut in _shortcuts)
            foreach (var configuration in shortcut.Configurations.Where(a => actionName.Equals(a.Name)).ToList())
                shortcut.Configurations.Remove(configuration);

        Logger.Trace("Unregistered \"{0}\" action", actionName);
    }

    public bool ActionAcceptsGestureData(string actionName, Type gestureDataType)
    {
        if (!_actions.TryGetValue(actionName, out var action))
            return false;

        return action.AcceptsGestureData(gestureDataType);
    }

    public IShortcutActionConfiguration CreateShortcutActionConfigurationInstance(string actionName)
    {
        if (!_actionConfigurationBuilders.TryGetValue(actionName, out var builder))
            return null;

        return builder.Build();
    }

    public void Invoke(string actionName, params object[] arguments)
    {
        Logger.Trace("Invoking \"{name}\" action [Arguments: \"{arguments}\"]", actionName, arguments);
        if (_actions.TryGetValue(actionName, out var action))
            action.Invoke(arguments);
    }

    public void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData)
    {
        Logger.Trace(() => $"Invoking \"{actionConfiguration.Name}\" action [Configuration: \"{string.Join(", ", actionConfiguration.Settings.Select(s => s.ToString()))}\", Gesture: {gestureData}]");
        if (!_actions.TryGetValue(actionConfiguration.Name, out var action))
            return;

        action.Invoke(actionConfiguration, gestureData);
    }

    public void Invoke(string actionName)
    {
        Logger.Trace("Invoking \"{0}\" action", actionName);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction concreteAction)
            concreteAction.Invoke();
    }

    public void Invoke<T0>(string actionName, T0 arg0)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}\"]", actionName, arg0);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction<T0> concreteAction)
            concreteAction.Invoke(arg0);
    }

    public void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}\"]", actionName, arg0, arg1);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction<T0, T1> concreteAction)
            concreteAction.Invoke(arg0, arg1);
    }

    public void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}\"]", actionName, arg0, arg1, arg2);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction<T0, T1, T2> concreteAction)
            concreteAction.Invoke(arg0, arg1, arg2);
    }

    public void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}\"]", actionName, arg0, arg1, arg2, arg3);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction<T0, T1, T2, T3> concreteAction)
            concreteAction.Invoke(arg0, arg1, arg2, arg3);
    }

    public void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}, {5}\"]", actionName, arg0, arg1, arg2, arg3, arg4);
        if (_actions.TryGetValue(actionName, out var action) && action is ShortcutAction<T0, T1, T2, T3, T4> concreteAction)
            concreteAction.Invoke(arg0, arg1, arg2, arg3, arg4);
    }

    private void HandleGesture(object sender, IInputGesture gesture)
    {
        if (!HandleGestures)
            return;

        foreach (var shortcut in _shortcuts)
        {
            if (shortcut.Configurations.Count == 0)
                continue;
            if (!shortcut.Enabled)
                continue;

            var gestureData = shortcut.CreateData(gesture);
            if (gestureData == null)
                continue;

            Logger.Trace("Invoking shortcut actions [Type: {0}, Gesture: {1}]", shortcut, shortcut.Gesture);
            foreach (var configuration in shortcut.Configurations)
                Invoke(configuration, gestureData);
        }
    }

    private void Dispose(bool disposing)
    {
        _inputManager.OnGesture -= HandleGesture;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
