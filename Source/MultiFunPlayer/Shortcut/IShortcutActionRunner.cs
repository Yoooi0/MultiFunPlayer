using MultiFunPlayer.Input;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutActionRunner
{
    bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback);

    void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData);
    void Invoke(string actionName, params object[] arguments);
    void Invoke(string actionName);
    void Invoke<T0>(string actionName, T0 arg0);
    void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1);
    void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2);
    void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
    void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);

    ValueTask InvokeAsync(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData);
    ValueTask InvokeAsync(string actionName, params object[] arguments);
    ValueTask InvokeAsync(string actionName);
    ValueTask InvokeAsync<T0>(string actionName, T0 arg0);
    ValueTask InvokeAsync<T0, T1>(string actionName, T0 arg0, T1 arg1);
    ValueTask InvokeAsync<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2);
    ValueTask InvokeAsync<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
    ValueTask InvokeAsync<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
}

internal class ShortcutActionRunner : IShortcutActionRunner, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IShortcutActionResolver _actionResolver;

    private BlockingCollection<ScheduledItem> _queue;
    private Thread _thread;

    public ShortcutActionRunner(IShortcutActionResolver actionResolver)
    {
        _actionResolver = actionResolver;
        _queue = [];

        _thread = new Thread(ConsumeItems);
        _thread.Start();
    }

    private void ConsumeItems()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            foreach (var configuration in item.Configurations)
            {
                if (_actionResolver.TryGetAction(configuration.Name, out var action))
                {
                    Logger.Trace(() => $"Invoking \"{configuration.Name}\" action [Configuration: \"{string.Join(", ", configuration.Settings.Select(s => s.ToString()))}\", Gesture: {item.GestureData}]");
                    var valueTask = action.Invoke(configuration, item.GestureData);
                    if (!valueTask.IsCompleted)
                        valueTask.AsTask().GetAwaiter().GetResult();
                }
            }

            item.Callback?.Invoke();
        }
    }

    public bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback)
        => _queue.TryAdd(new ScheduledItem(configurations, gestureData, callback));

    public void Invoke(string actionName, params object[] arguments)
    {
        var task = InvokeAsync(actionName, arguments);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData)
    {
        var task = InvokeAsync(actionConfiguration, gestureData);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke(string actionName)
    {
        var task = InvokeAsync(actionName);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke<T0>(string actionName, T0 arg0)
    {
        var task = InvokeAsync(actionName, arg0);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke<T0, T1>(string actionName, T0 arg0, T1 arg1)
    {
        var task = InvokeAsync(actionName, arg0, arg1);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2)
    {
        var task = InvokeAsync(actionName, arg0, arg1, arg2);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        var task = InvokeAsync(actionName, arg0, arg1, arg2, arg3);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public void Invoke<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        var task = InvokeAsync(actionName, arg0, arg1, arg2, arg3, arg4);
        if (!task.IsCompleted)
            task.AsTask().GetAwaiter().GetResult();
    }

    public ValueTask InvokeAsync(string actionName, params object[] arguments)
    {
        Logger.Trace("Invoking \"{name}\" action [Arguments: \"{arguments}\"]", actionName, arguments);
        if (_actionResolver.TryGetAction(actionName, out var action))
            return action.Invoke(arguments);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync(IShortcutActionConfiguration actionConfiguration, IInputGestureData gestureData)
    {
        Logger.Trace(() => $"Invoking \"{actionConfiguration.Name}\" action [Configuration: \"{string.Join(", ", actionConfiguration.Settings.Select(s => s.ToString()))}\", Gesture: {gestureData}]");
        if (_actionResolver.TryGetAction(actionConfiguration.Name, out var action))
            return action.Invoke(actionConfiguration, gestureData);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync(string actionName)
    {
        Logger.Trace("Invoking \"{0}\" action", actionName);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction concreteAction)
            return concreteAction.Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync<T0>(string actionName, T0 arg0)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}\"]", actionName, arg0);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction<T0> concreteAction)
            return concreteAction.Invoke(arg0);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync<T0, T1>(string actionName, T0 arg0, T1 arg1)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}\"]", actionName, arg0, arg1);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction<T0, T1> concreteAction)
            return concreteAction.Invoke(arg0, arg1);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}\"]", actionName, arg0, arg1, arg2);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction<T0, T1, T2> concreteAction)
            return concreteAction.Invoke(arg0, arg1, arg2);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}\"]", actionName, arg0, arg1, arg2, arg3);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction<T0, T1, T2, T3> concreteAction)
            return concreteAction.Invoke(arg0, arg1, arg2, arg3);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvokeAsync<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Logger.Trace("Invoking \"{0}\" action [Arguments: \"{1}, {2}, {3}, {4}, {5}\"]", actionName, arg0, arg1, arg2, arg3, arg4);
        if (_actionResolver.TryGetAction(actionName, out var action) && action is ShortcutAction<T0, T1, T2, T3, T4> concreteAction)
            return concreteAction.Invoke(arg0, arg1, arg2, arg3, arg4);
        return ValueTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        _queue?.CompleteAdding();
        _thread?.Join();
        _queue?.Dispose();

        _queue = null;
        _thread = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private record struct ScheduledItem(IEnumerable<IShortcutActionConfiguration> Configurations, IInputGestureData GestureData, Action Callback);
}