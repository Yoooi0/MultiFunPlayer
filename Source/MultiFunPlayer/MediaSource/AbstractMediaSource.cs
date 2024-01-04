using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Channels;

namespace MultiFunPlayer.MediaSource;

internal abstract class AbstractMediaSource : Screen, IMediaSource, IHandle<IMediaSourceControlMessage>
{
    private readonly Channel<IMediaSourceControlMessage> _messageChannel;
    private readonly IEventAggregator _eventAggregator;
    private CancellationTokenSource _cancellationSource;
    private Task _task;

    public string Name { get; init; }
    [SuppressPropertyChangedWarnings] public abstract ConnectionStatus Status { get; protected set; }
    public bool AutoConnectEnabled { get; set; } = false;

    protected bool IsDisposing { get; private set; }

    protected AbstractMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
    {
        _messageChannel = Channel.CreateUnbounded<IMediaSourceControlMessage>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);

        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        RegisterActions(shortcutManager);
    }

    protected abstract Task RunAsync(CancellationToken token);

    protected void PublishMessage(object message) => _eventAggregator.Publish(message);

    public async virtual Task ConnectAsync()
    {
        if (Status != ConnectionStatus.Disconnected)
            return;

        Status = ConnectionStatus.Connecting;
        if (!await OnConnectingAsync())
            await DisconnectAsync();
    }

    protected virtual async ValueTask<bool> OnConnectingAsync()
    {
        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
        _ = _task.ContinueWith(_ => DisconnectAsync()).Unwrap();

        return await ValueTask.FromResult(true);
    }

    public async virtual Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
            return;

        Status = ConnectionStatus.Disconnecting;
        await Task.Delay(250);
        await OnDisconnectingAsync();
        Status = ConnectionStatus.Disconnected;
    }

    private int _isDisconnectingFlag;
    protected virtual async ValueTask OnDisconnectingAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisconnectingFlag, 1, 0) != 0)
            return;

        _cancellationSource?.Cancel();
        if (_task != null)
            await _task;

        _cancellationSource?.Dispose();
        _cancellationSource = null;
        _task = null;

        Interlocked.Decrement(ref _isDisconnectingFlag);
    }

    public async virtual ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false);
    public async virtual ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token)
    {
        if (Status != ConnectionStatus.Disconnected)
            return await ValueTask.FromResult(false);

        Status = ConnectionStatus.Connecting;
        await Task.Delay(100, token);
        var result = await CanConnectAsync(token);
        Status = ConnectionStatus.Disconnected;

        return result;
    }

    public async Task WaitForStatus(IEnumerable<ConnectionStatus> statuses, CancellationToken token)
    {
        var channel = Channel.CreateUnbounded<ConnectionStatus>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        PropertyChanged += OnPropertyChanged;

        while (true)
            if (statuses.Contains(Status) || statuses.Contains(await channel.Reader.ReadAsync(token)))
                break;

        PropertyChanged -= OnPropertyChanged;

        void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                channel.Writer.TryWrite(Status);
        }
    }

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            settings[nameof(AutoConnectEnabled)] = AutoConnectEnabled;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(AutoConnectEnabled), out var autoConnectEnabled))
                AutoConnectEnabled = autoConnectEnabled;
        }
    }

    protected virtual void RegisterActions(IShortcutManager s)
    {
        #region AutoConnectEnabled
        s.RegisterAction<bool>($"{Name}::AutoConnectEnabled::Set", s => s.WithLabel("Enable auto connect"), enabled => AutoConnectEnabled = enabled);
        s.RegisterAction($"{Name}::AutoConnectEnabled::Toggle", () => AutoConnectEnabled = !AutoConnectEnabled);
        #endregion
    }

    protected void ClearPendingMessages()
    {
        while (_messageChannel.Reader.TryRead(out _)) ;
    }

    protected async ValueTask WaitForMessageAsync(CancellationToken token)
        => await _messageChannel.Reader.WaitToReadAsync(token);

    protected async ValueTask<IMediaSourceControlMessage> ReadMessageAsync(CancellationToken token)
        => await _messageChannel.Reader.ReadAsync(token);

    protected bool TryReadMessage(out IMediaSourceControlMessage message)
        => _messageChannel.Reader.TryRead(out message);

    protected void WriteMessage(IMediaSourceControlMessage message)
        => _messageChannel.Writer.TryWrite(message);

    public void Handle(IMediaSourceControlMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            _messageChannel.Writer.TryWrite(message);
    }

    protected virtual void Dispose(bool disposing)
    {
        var valueTask = OnDisconnectingAsync();
        if (!valueTask.IsCompleted)
            valueTask.AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        IsDisposing = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
