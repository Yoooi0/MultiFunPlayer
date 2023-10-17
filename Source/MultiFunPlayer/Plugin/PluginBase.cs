using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using NLog;
using Stylet;
using StyletIoC;
using System.Diagnostics.CodeAnalysis;

namespace MultiFunPlayer.Plugin;

public abstract class PluginBase : PropertyChangedBase
{
    private readonly MessageProxy _messageProxy;

    internal event EventHandler<Exception> OnInternalException;

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

    #region DeviceAxis
    protected double GetAxisValue(DeviceAxis axis)
        => DeviceAxisValueProvider.GetValue(axis);
    #endregion

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

    protected IShortcutBinding GetOrCreateBinding(IInputGestureDescriptor gestureDescriptor)
        => ShortcutBinder.GetOrCreateBinding(gestureDescriptor);
    protected void AddBinding(IShortcutBinding binding)
        => ShortcutBinder.AddBinding(binding);
    protected bool RemoveBinding(IShortcutBinding binding)
        => ShortcutBinder.RemoveBinding(binding);
    protected bool RemoveBinding(IInputGestureDescriptor gestureDescriptor)
        => ShortcutBinder.RemoveBinding(gestureDescriptor);
    #endregion

    #region Message
    protected void PublishMessage(MediaSpeedChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPositionChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayingChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPathChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaDurationChangedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaSeekMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaPlayPauseMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaChangePathMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(MediaChangeSpeedMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(ChangeScriptMessage message) => EventAggregator.Publish(message);
    protected void PublishMessage(SyncRequestMessage message) => EventAggregator.Publish(message);

    protected virtual void HandleMessage(MediaSpeedChangedMessage message) { }
    protected virtual void HandleMessage(MediaPositionChangedMessage message) { }
    protected virtual void HandleMessage(MediaPlayPauseMessage message) { }
    protected virtual void HandleMessage(MediaPathChangedMessage message) { }
    protected virtual void HandleMessage(MediaDurationChangedMessage message) { }
    protected virtual void HandleMessage(MediaSeekMessage message) { }
    protected virtual void HandleMessage(MediaPlayingChangedMessage message) { }
    protected virtual void HandleMessage(MediaChangePathMessage message) { }
    protected virtual void HandleMessage(MediaChangeSpeedMessage message) { }
    protected virtual void HandleMessage(ScriptChangedMessage message) { }
    protected virtual void HandleMessage(SyncRequestMessage message) { }

    private void HandleMessageInternal(object e)
    {
        try
        {
            if (e is MediaSpeedChangedMessage mediaSpeedChangedMessage) HandleMessage(mediaSpeedChangedMessage);
            else if (e is MediaPositionChangedMessage mediaPositionChangedMessage) HandleMessage(mediaPositionChangedMessage);
            else if (e is MediaPlayingChangedMessage mediaPlayingChangedMessage) HandleMessage(mediaPlayingChangedMessage);
            else if (e is MediaPathChangedMessage mediaPathChangedMessage) HandleMessage(mediaPathChangedMessage);
            else if (e is MediaDurationChangedMessage mediaDurationChangedMessage) HandleMessage(mediaDurationChangedMessage);
            else if (e is MediaSeekMessage mediaSeekMessage) HandleMessage(mediaSeekMessage);
            else if (e is MediaPlayPauseMessage mediaPlayPauseMessage) HandleMessage(mediaPlayPauseMessage);
            else if (e is MediaChangePathMessage mediaChangePathMessage) HandleMessage(mediaChangePathMessage);
            else if (e is MediaChangeSpeedMessage mediaChangeSpeedMessage) HandleMessage(mediaChangeSpeedMessage);
            else if (e is ScriptChangedMessage scriptChangedMessage) HandleMessage(scriptChangedMessage);
            else if (e is SyncRequestMessage syncRequestMessage) HandleMessage(syncRequestMessage);
        }
        catch (Exception exception)
        {
            OnInternalException?.Invoke(this, exception);
        }
    }

    private class MessageProxy : IHandle<object>
    {
        private readonly Action<object> _callback;
        public MessageProxy(Action<object> callback) => _callback = callback;
        public void Handle(object message) => _callback(message);
    }
    #endregion

    internal void InternalInitialize()
    {
        EventAggregator.Subscribe(_messageProxy);
    }

    protected virtual void Dispose(bool disposing) { }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "Internal dispose")]
    internal void InternalDispose()
    {
        EventAggregator.Unsubscribe(_messageProxy);
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public abstract class SyncPluginBase : PluginBase
{
    protected virtual void Execute(CancellationToken cancellationToken)
    {
        try { cancellationToken.WaitHandle.WaitOne(); }
        catch { }

        throw new OperationCanceledException();
    }

    internal void InternalExecute(CancellationToken cancellationToken)
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var internalException = default(Exception);

        void HandleInternalException(object _, Exception e)
        {
            if (Interlocked.CompareExchange(ref internalException, e, null) == null)
                cancellationSource.Cancel();
        }

        try
        {
            OnInternalException += HandleInternalException;
            Execute(cancellationSource.Token);
        }
        catch (OperationCanceledException) when (internalException != null) { internalException.Throw(); }
        catch (Exception e) when (internalException != null) { throw new AggregateException(internalException, e); }
        finally { OnInternalException -= HandleInternalException; }
    }
}

public abstract class AsyncPluginBase : PluginBase
{
    protected virtual async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await Task.FromCanceled(cancellationToken);
            return;
        }

        var taskCompletionSource = new TaskCompletionSource();
        using var registration = cancellationToken.Register(() => taskCompletionSource.TrySetCanceled(cancellationToken), useSynchronizationContext: false);

        try { await taskCompletionSource.Task; }
        finally { await registration.DisposeAsync(); }
    }

    internal async Task InternalExecuteAsync(CancellationToken cancellationToken)
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var internalException = default(Exception);

        void HandleInternalException(object _, Exception e)
        {
            if (Interlocked.CompareExchange(ref internalException, e, null) == null)
                cancellationSource.Cancel();
        }

        try
        {
            OnInternalException += HandleInternalException;
            await ExecuteAsync(cancellationSource.Token);
        }
        catch (OperationCanceledException) when (internalException != null) { internalException.Throw(); }
        catch (Exception e) when (internalException != null) { throw new AggregateException(internalException, e); }
        finally { OnInternalException -= HandleInternalException; }
    }
}