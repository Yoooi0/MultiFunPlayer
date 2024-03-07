using MultiFunPlayer.Input;
using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutActionRunner
{
    bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback);
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
        => _queue.TryAdd(new ScheduledItem(configurations, gestureData, callback)); //TODO: configurations.ToList?

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