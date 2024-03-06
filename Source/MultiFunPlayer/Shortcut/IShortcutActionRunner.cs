using MultiFunPlayer.Input;
using NLog;
using System.Threading.Channels;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutActionRunner
{
    bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback);
}

internal class ShortcutActionRunner : IShortcutActionRunner, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IShortcutActionResolver _actionResolver;
    private readonly Channel<ScheduledItem> _itemChannel;

    private CancellationTokenSource _cancellationSource;
    private Task _task;

    public ShortcutActionRunner(IShortcutActionResolver actionResolver)
    {
        _actionResolver = actionResolver;
        _itemChannel = Channel.CreateUnbounded<ScheduledItem>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => InvokeAsync(_cancellationSource.Token),
                    _cancellationSource.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                        .Unwrap();
    }

    private async Task InvokeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var item in _itemChannel.Reader.ReadAllAsync(token))
            {
                foreach (var configuration in item.Configurations)
                {
                    if (_actionResolver.TryGetAction(configuration.Name, out var action))
                    {
                        Logger.Trace(() => $"Invoking \"{configuration.Name}\" action [Configuration: \"{string.Join(", ", configuration.Settings.Select(s => s.ToString()))}\", Gesture: {item.GestureData}]");
                        await action.Invoke(configuration, item.GestureData);
                    }
                }

                item.Callback?.Invoke();
            }
        } catch (OperationCanceledException) { }
    }

    public bool ScheduleInvoke(IEnumerable<IShortcutActionConfiguration> configurations, IInputGestureData gestureData, Action callback)
        => _itemChannel.Writer.TryWrite(new ScheduledItem(configurations, gestureData, callback)); //TODO: configurations.ToList?

    protected virtual void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();
        _task?.GetAwaiter().GetResult();
        _cancellationSource?.Dispose();

        _task = null;
        _cancellationSource = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private record struct ScheduledItem(IEnumerable<IShortcutActionConfiguration> Configurations, IInputGestureData GestureData, Action Callback);
}