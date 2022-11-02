using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using StyletIoC;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace MultiFunPlayer.Plugin;

public abstract class PluginBase : PropertyChangedBase
{
    internal readonly MessageProxy _messageProxy;

    [Inject] internal IDeviceAxisValueProvider DeviceAxisValueProvider { get; set; }
    [Inject] internal IEventAggregator EventAggregator { get; set; }
    [Inject] internal IShortcutManager ShortcutManager { get; set; }

    public virtual string Name => GetType().Name;
    protected Logger Logger { get; }

    protected PluginBase()
    {
        _messageProxy = new(HandleMessageInternal);
        Logger = LogManager.GetLogger(GetType().FullName);
    }

    public virtual UIElement CreateView() => null;
    public virtual void HandleSettings(JObject settings, SettingsAction action) { }

    protected UIElement CreateViewFromStream(Stream stream) => XamlReader.Load(stream) as UIElement;
    protected UIElement CreateViewFromFile(string path) => CreateViewFromStream(File.OpenRead(path));
    protected UIElement CreateViewFromString(string xamlContent)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xamlContent));
        return CreateViewFromStream(stream);
    }

    protected double GetAxisValue(DeviceAxis axis)
        => DeviceAxisValueProvider.GetValue(axis);

    protected void InvokeAction(string name, params object[] arguments)
        => ShortcutManager.Invoke(name, arguments);

    protected void PublishMessage(MediaSpeedChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPositionChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayingChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPathChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaDurationChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaSeekMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayPauseMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(ScriptLoadMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(SyncRequestMessage message) => EventAggregator.Publish(message);

    protected virtual void HandleMessage(MediaSpeedChangedMessage message) { }
    protected virtual void HandleMessage(MediaPositionChangedMessage message) { }
    protected virtual void HandleMessage(MediaPlayPauseMessage message) { }
    protected virtual void HandleMessage(MediaPathChangedMessage message) { }
    protected virtual void HandleMessage(MediaDurationChangedMessage message) { }
    protected virtual void HandleMessage(MediaSeekMessage message) { }
    protected virtual void HandleMessage(MediaPlayingChangedMessage message) { }
    protected virtual void HandleMessage(ScriptLoadMessage message) { }
    protected virtual void HandleMessage(SyncRequestMessage message) { }

    internal void OnEventAggregatorChanged() => EventAggregator.Subscribe(_messageProxy);
    internal void HandleMessageInternal(object e)
    {
        if (e is MediaSpeedChangedMessage mediaSpeedChangedMessage) HandleMessage(mediaSpeedChangedMessage);
        else if (e is MediaPositionChangedMessage mediaPositionChangedMessage) HandleMessage(mediaPositionChangedMessage);
        else if (e is MediaPlayingChangedMessage mediaPlayingChangedMessage) HandleMessage(mediaPlayingChangedMessage);
        else if (e is MediaPathChangedMessage mediaPathChangedMessage) HandleMessage(mediaPathChangedMessage);
        else if (e is MediaDurationChangedMessage mediaDurationChangedMessage) HandleMessage(mediaDurationChangedMessage);
        else if (e is MediaSeekMessage mediaSeekMessage) HandleMessage(mediaSeekMessage);
        else if (e is MediaPlayPauseMessage mediaPlayPauseMessage) HandleMessage(mediaPlayPauseMessage);
        else if (e is ScriptLoadMessage scriptLoadMessage) HandleMessage(scriptLoadMessage);
        else if (e is SyncRequestMessage syncRequestMessage) HandleMessage(syncRequestMessage);
    }

    internal class MessageProxy : IHandle<object>
    {
        private readonly Action<object> _callback;
        public MessageProxy(Action<object> callback) => _callback = callback;
        public void Handle(object message) => _callback(message);
    }
}

public abstract class SyncPluginBase : PluginBase
{
    public abstract void Execute(CancellationToken cancellationToken);
}

public abstract class AsyncPluginBase : PluginBase
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}