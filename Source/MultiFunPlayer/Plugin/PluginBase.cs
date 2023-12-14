using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Property;
using NLog;
using Stylet;
using StyletIoC;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MultiFunPlayer.Plugin;

public abstract class PluginBase : PropertyChangedBase
{
    private readonly MessageProxy _messageProxy;
    private readonly FrozenDictionary<Type, bool> _asyncHandlerOverrides;
    internal CancellationTokenSource _internalCancellationSource;

    internal event EventHandler<Exception> OnInternalException;

    [Inject] internal IDeviceAxisValueProvider DeviceAxisValueProvider { get; set; }
    [Inject] internal IEventAggregator EventAggregator { get; set; }
    [Inject] internal IShortcutManager ShortcutManager { get; set; }
    [Inject] internal IShortcutBinder ShortcutBinder { get; set; }
    [Inject] internal IPropertyManager PropertyManager { get; set; }

    protected Logger Logger { get; }

    protected PluginBase()
    {
        _messageProxy = new(HandleMessageInternal);
        Logger = LogManager.GetLogger(GetType().FullName);

        _asyncHandlerOverrides = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                                          .Where(m => m.Name == nameof(HandleMessageAsync))
                                          .ToFrozenDictionary(m => m.GetParameters()[0].ParameterType,
                                                              m => m.GetBaseDefinition().DeclaringType != m.DeclaringType);
    }

    #region DeviceAxis
    protected double GetAxisValue(DeviceAxis axis)
        => DeviceAxisValueProvider.GetValue(axis);
    #endregion

    #region Shortcut
    protected void InvokeAction(string actionName, params object[] arguments)
        => ShortcutManager.Invoke(actionName, arguments);

    protected void InvokeAction(string actionName)
        => ShortcutManager.Invoke(actionName);
    protected void InvokeAction<T0>(string actionName, T0 arg0)
        => ShortcutManager.Invoke(actionName, arg0);
    protected void InvokeAction<T0, T1>(string actionName, T0 arg0, T1 arg1)
        => ShortcutManager.Invoke(actionName, arg0, arg1);
    protected void InvokeAction<T0, T1, T2>(string actionName, T0 arg0, T1 arg1, T2 arg2)
        => ShortcutManager.Invoke(actionName, arg0, arg1, arg2);
    protected void InvokeAction<T0, T1, T2, T3>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        => ShortcutManager.Invoke(actionName, arg0, arg1, arg2, arg3);
    protected void InvokeAction<T0, T1, T2, T3, T4>(string actionName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => ShortcutManager.Invoke(actionName, arg0, arg1, arg2, arg3, arg4);

    protected void RegisterAction<T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<T0> action)
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<T0, T1> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<T0, T1, T2> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<T0, T1, T2, T3> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);
    protected void RegisterAction<T0, T1, T2, T3, T4>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Func<IShortcutSettingBuilder<T4>, IShortcutSettingBuilder<T4>> settings4, Action<T0, T1, T2, T3, T4> action)
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, settings4, action);
    protected void RegisterAction<TG>(string actionName, Action<TG> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(actionName, action);
    protected void RegisterAction<TG, T0>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Action<TG, T0> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(actionName, settings0, action);
    protected void RegisterAction<TG, T0, T1>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Action<TG, T0, T1> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, action);
    protected void RegisterAction<TG, T0, T1, T2>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Action<TG, T0, T1, T2> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, action);
    protected void RegisterAction<TG, T0, T1, T2, T3>(string actionName, Func<IShortcutSettingBuilder<T0>, IShortcutSettingBuilder<T0>> settings0, Func<IShortcutSettingBuilder<T1>, IShortcutSettingBuilder<T1>> settings1, Func<IShortcutSettingBuilder<T2>, IShortcutSettingBuilder<T2>> settings2, Func<IShortcutSettingBuilder<T3>, IShortcutSettingBuilder<T3>> settings3, Action<TG, T0, T1, T2, T3> action) where TG : IInputGesture
        => ShortcutManager.RegisterAction(actionName, settings0, settings1, settings2, settings3, action);

    protected void UnregisterAction(string actionName) => ShortcutManager.UnregisterAction(actionName);
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

    #region Property
    protected TOut ReadProperty<TOut>(string propertyName, params object[] arguments) => PropertyManager.GetValue<TOut>(propertyName, arguments);
    protected TOut ReadProperty<TOut>(string propertyName) => PropertyManager.GetValue<TOut>(propertyName);
    protected TOut ReadProperty<T0, TOut>(string propertyName, T0 arg0) => PropertyManager.GetValue<T0, TOut>(propertyName, arg0);
    protected TOut ReadProperty<T0, T1, TOut>(string propertyName, T0 arg0, T1 arg1) => PropertyManager.GetValue<T0, T1, TOut>(propertyName, arg0, arg1);
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
    protected virtual void HandleMessage(PostScriptSearchMessage message) { }

    protected virtual Task HandleMessageAsync(MediaSpeedChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaPositionChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaPlayPauseMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaPathChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaDurationChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaSeekMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaPlayingChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaChangePathMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(MediaChangeSpeedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(ScriptChangedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(SyncRequestMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task HandleMessageAsync(PostScriptSearchMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

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
            else if (e is PostScriptSearchMessage postScriptSearchMessage) HandleMessage(postScriptSearchMessage);
        }
        catch (Exception exception)
        {
            OnInternalException?.Invoke(this, exception);
        }

        if (_asyncHandlerOverrides.TryGetValue(e.GetType(), out var overridden) && overridden)
        {
            var token = _internalCancellationSource.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (e is MediaSpeedChangedMessage mediaSpeedChangedMessage) await HandleMessageAsync(mediaSpeedChangedMessage, token);
                    else if (e is MediaPositionChangedMessage mediaPositionChangedMessage) await HandleMessageAsync(mediaPositionChangedMessage, token);
                    else if (e is MediaPlayingChangedMessage mediaPlayingChangedMessage) await HandleMessageAsync(mediaPlayingChangedMessage, token);
                    else if (e is MediaPathChangedMessage mediaPathChangedMessage) await HandleMessageAsync(mediaPathChangedMessage, token);
                    else if (e is MediaDurationChangedMessage mediaDurationChangedMessage) await HandleMessageAsync(mediaDurationChangedMessage, token);
                    else if (e is MediaSeekMessage mediaSeekMessage) await HandleMessageAsync(mediaSeekMessage, token);
                    else if (e is MediaPlayPauseMessage mediaPlayPauseMessage) await HandleMessageAsync(mediaPlayPauseMessage, token);
                    else if (e is MediaChangePathMessage mediaChangePathMessage) await HandleMessageAsync(mediaChangePathMessage, token);
                    else if (e is MediaChangeSpeedMessage mediaChangeSpeedMessage) await HandleMessageAsync(mediaChangeSpeedMessage, token);
                    else if (e is ScriptChangedMessage scriptChangedMessage) await HandleMessageAsync(scriptChangedMessage, token);
                    else if (e is SyncRequestMessage syncRequestMessage) await HandleMessageAsync(syncRequestMessage, token);
                    else if (e is PostScriptSearchMessage postScriptSearchMessage) await HandleMessageAsync(postScriptSearchMessage, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    OnInternalException?.Invoke(this, exception);
                }
            });
        }
    }

    private sealed class MessageProxy(Action<object> callback) : IHandle<object>
    {
        public void Handle(object message) => callback(message);
    }
    #endregion

    internal void InternalInitialize(CancellationToken cancellationToken)
    {
        _internalCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        EventAggregator.Subscribe(_messageProxy);
    }

    protected virtual void Dispose(bool disposing) { }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "Internal dispose")]
    internal void InternalDispose()
    {
        EventAggregator.Unsubscribe(_messageProxy);

        _internalCancellationSource?.Cancel();
        _internalCancellationSource?.Dispose();
        _internalCancellationSource = null;

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

    internal void InternalExecute()
    {
        var internalException = default(Exception);
        void HandleInternalException(object _, Exception e)
        {
            if (Interlocked.CompareExchange(ref internalException, e, null) == null)
                _internalCancellationSource.Cancel();
        }

        try
        {
            OnInternalException += HandleInternalException;
            Execute(_internalCancellationSource.Token);
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
        await using var registration = cancellationToken.Register(() => taskCompletionSource.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
        await taskCompletionSource.Task;
    }

    internal async Task InternalExecuteAsync()
    {
        var internalException = default(Exception);
        void HandleInternalException(object _, Exception e)
        {
            if (Interlocked.CompareExchange(ref internalException, e, null) == null)
                _internalCancellationSource.Cancel();
        }

        try
        {
            OnInternalException += HandleInternalException;
            await ExecuteAsync(_internalCancellationSource.Token);
        }
        catch (OperationCanceledException) when (internalException != null) { internalException.Throw(); }
        catch (Exception e) when (internalException != null) { throw new AggregateException(internalException, e); }
        finally { OnInternalException -= HandleInternalException; }
    }
}