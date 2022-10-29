using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using NLog;
using Stylet;
using StyletIoC;

namespace MultiFunPlayer.Plugin;

public interface IPlugin
{
    string Name { get; }
}

public interface ISyncPlugin : IPlugin
{
    public void Execute(CancellationToken cancellationToken);
}

public interface IAsyncPlugin : IPlugin
{
    public Task ExecuteAsync(CancellationToken cancellationToken);
}

public abstract class AbstractPlugin : IPlugin
{
    [Inject] internal IEventAggregator EventAggregator { get; set; }
    [Inject] internal IShortcutManager ShortcutManager { get; set; }

    public string Name => GetType().Name;

    protected Logger Logger { get; }

    protected AbstractPlugin()
    {
        Logger = LogManager.GetLogger(GetType().FullName);
    }

    protected void InvokeAction(string name, params object[] arguments)
        => ShortcutManager.Invoke(name, arguments);

    protected void PublishMediaSpeedMessage(double speed)
        => EventAggregator.Publish(new MediaSpeedChangedMessage(speed));
    protected void PublishMediaSeekMessage(TimeSpan? position)
        => EventAggregator.Publish(new MediaSeekMessage(position));
    protected void PublishMediaPositionMessage(TimeSpan? position, bool forceSeek = false)
        => EventAggregator.Publish(new MediaPositionChangedMessage(position, forceSeek));
    protected void PublishMediaPlayPauseMessage(bool state)
        => EventAggregator.Publish(new MediaPlayPauseMessage(state));
    protected void PublishMediaPlayingMessage(bool isPlaying)
        => EventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying));
    protected void PublishMediaPathMessage(string path)
        => EventAggregator.Publish(new MediaPathChangedMessage(path));
    protected void PublishMediaDurationMessage(TimeSpan? duration)
        => EventAggregator.Publish(new MediaDurationChangedMessage(duration));
}

public abstract class SyncPluginBase : AbstractPlugin, ISyncPlugin
{
    public abstract void Execute(CancellationToken cancellationToken);
}

public abstract class AsyncPluginBase : AbstractPlugin, IAsyncPlugin
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}