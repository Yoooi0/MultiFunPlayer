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
    private readonly MessageProxy _messageProxy;

    [Inject] internal IDeviceAxisValueProvider DeviceAxisValueProvider { get; set; }
    [Inject] internal IEventAggregator EventAggregator { get; set; }
    [Inject] internal IShortcutManager ShortcutManager { get; set; }
    [Inject] internal IShortcutBinder ShortcutBinder { get; set; }

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

    #region Shortcut
    protected void InvokeAction(string name, params object[] arguments)
        => ShortcutManager.Invoke(name, arguments);

    protected void RegisterAction<T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action)
        => ShortcutManager.RegisterAction(name, settings0, action);
    protected void RegisterAction<T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action)
        => ShortcutManager.RegisterAction(name, settings0, settings1, action);
    protected void RegisterAction<T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action)
        => ShortcutManager.RegisterAction(name, settings0, settings1, settings2, action);
    protected void RegisterAction<T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action)
        => ShortcutManager.RegisterAction(name, settings0, settings1, settings2, settings3, action);
    protected void RegisterAction<T0, T1, T2, T3, T4>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action)
        => ShortcutManager.RegisterAction(name, settings0, settings1, settings2, settings3, settings4, action);
    protected void RegisterAction<TG>(string name, Action<TG> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(name, action);
    protected void RegisterAction<TG, T0>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TG, T0> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(name, settings0, action);
    protected void RegisterAction<TG, T0, T1>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TG, T0, T1> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(name, settings0, settings1, action);
    protected void RegisterAction<TG, T0, T1, T2>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TG, T0, T1, T2> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(name, settings0, settings1, settings2, action);
    protected void RegisterAction<TG, T0, T1, T2, T3>(string name, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TG, T0, T1, T2, T3> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(name, settings0, settings1, settings2, settings3, action);

    protected void UnregisterAction(string name) => ShortcutManager.UnregisterAction(name);
    #endregion

    #region Binding
    protected IShortcutActionConfiguration BindAction(IInputGestureDescriptor gestureDescriptor, string actionName, params object[] values)
        => ShortcutBinder.BindActionWithSettings(gestureDescriptor, actionName, values);
    protected void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionConfiguration action)
        => ShortcutBinder.UnbindAction(gestureDescriptor, action);

    protected void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
        => ShortcutBinder.RegisterGesture(gestureDescriptor);
    protected void UnregisterGesture(IInputGestureDescriptor gestureDescriptor)
        => ShortcutBinder.UnregisterGesture(gestureDescriptor);
    #endregion

    #region Message
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
    #endregion
}

public abstract class SyncPluginBase : PluginBase
{
    public abstract void Execute(CancellationToken cancellationToken);
}

public abstract class AsyncPluginBase : PluginBase
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}