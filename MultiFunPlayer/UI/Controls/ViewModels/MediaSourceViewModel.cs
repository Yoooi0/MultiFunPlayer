using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.MediaSource;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class MediaSourceViewModel : Conductor<IMediaSource>.Collection.OneActive, IHandle<SettingsMessage>, IDisposable
{
    private Task _task;
    private CancellationTokenSource _cancellationSource;
    private IMediaSource _currentSource;
    private SemaphoreSlim _semaphore;

    public bool ContentVisible { get; set; }
    public int ScanDelay { get; set; } = 2500;
    public int ScanInterval { get; set; } = 5000;

    public MediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IEnumerable<IMediaSource> sources)
    {
        eventAggregator.Subscribe(this);
        Items.AddRange(sources);

        _currentSource = null;

        _semaphore = new SemaphoreSlim(1, 1);
        _cancellationSource = new CancellationTokenSource();

        RegisterShortcuts(shortcutManager);
    }

    protected override void OnViewLoaded()
    {
        base.OnViewLoaded();

        if (_task != null)
            return;

        _task = Task.Factory.StartNew(() => ScanAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("MediaSource")
             || !message.Settings.TryGetObject(out var settings, "MediaSource"))
                return;

            settings[nameof(ContentVisible)] = ContentVisible;
            settings[nameof(ScanDelay)] = ScanDelay;
            settings[nameof(ScanInterval)] = ScanInterval;
            settings[nameof(ActiveItem)] = ActiveItem?.Name;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "MediaSource"))
                return;

            if (settings.TryGetValue<bool>(nameof(ContentVisible), out var contentVisible))
                ContentVisible = contentVisible;
            if (settings.TryGetValue<int>(nameof(ScanDelay), out var scanDelay))
                ScanDelay = scanDelay;
            if (settings.TryGetValue<int>(nameof(ScanInterval), out var scanInterval))
                ScanInterval = scanInterval;
            if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem, StringComparison.OrdinalIgnoreCase)) ?? Items.FirstOrDefault(), closePrevious: false);
        }
    }

    public async Task ToggleConnectAsync(IMediaSource source)
    {
        var token = _cancellationSource.Token;
        await _semaphore.WaitAsync(token);
        if (_currentSource == source)
            await ToggleConnectCurrentSourceAsync(token);
        else if (_currentSource != source)
            await ConnectAndSetAsCurrentSourceAsync(source, token);

        _semaphore.Release();
    }

    private async Task ToggleConnectCurrentSourceAsync(CancellationToken token)
    {
        if (_currentSource?.Status == ConnectionStatus.Connected)
            await DisconnectCurrentSourceAsync(token);
        else if (_currentSource?.Status == ConnectionStatus.Disconnected)
            await ConnectAsync(_currentSource, token);
    }

    private async Task ConnectAndSetAsCurrentSourceAsync(IMediaSource source, CancellationToken token)
    {
        if (_currentSource != null)
            await DisconnectCurrentSourceAsync(token);

        if (source != null)
            await ConnectAsync(source, token);

        if (source == null || source.Status == ConnectionStatus.Connected)
            _currentSource = source;
    }

    private async Task DisconnectCurrentSourceAsync(CancellationToken token)
    {
        await DisconnectAsync(_currentSource, token);
        _currentSource = null;
    }

    private async Task ConnectAsync(IMediaSource source, CancellationToken token)
    {
        await source.ConnectAsync();
        await source.WaitForIdle(token);
    }

    private async Task DisconnectAsync(IMediaSource source, CancellationToken token)
    {
        await source.DisconnectAsync();
        await source.WaitForDisconnect(token);
    }

    private async Task ScanAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ScanDelay, token);
            while (!token.IsCancellationRequested)
            {
                if (_currentSource != null)
                {
                    await _currentSource.WaitForDisconnect(token);
                    await _semaphore.WaitAsync(token);
                    if (_currentSource?.Status == ConnectionStatus.Disconnected)
                        _currentSource = null;
                    _semaphore.Release();
                }

                foreach (var source in Items.ToList())
                {
                    if (_currentSource != null)
                        break;

                    if (!source.AutoConnectEnabled)
                        continue;

                    if (await source.CanConnectAsyncWithStatus(token))
                    {
                        await _semaphore.WaitAsync(token);
                        if (_currentSource == null)
                        {
                            await source.ConnectAsync();
                            await source.WaitForIdle(token);

                            if (source.Status == ConnectionStatus.Connected)
                                _currentSource = source;
                        }

                        _semaphore.Release();
                    }
                }

                await Task.Delay(ScanInterval, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RegisterShortcuts(IShortcutManager s)
    {
        var token = _cancellationSource.Token;
        foreach (var source in Items)
        {
            s.RegisterAction($"{source.Name}::Connection::Toggle", b => b.WithCallback(async (_) => await ToggleConnectAsync(source)));
            s.RegisterAction($"{source.Name}::Connection::Connect", b => b.WithCallback(async (_) =>
            {
                await _semaphore.WaitAsync(token);
                if (_currentSource != source)
                    await ConnectAndSetAsCurrentSourceAsync(source, token);
                _semaphore.Release();
            }));
            s.RegisterAction($"{source.Name}::Connection::Disconnect", b => b.WithCallback(async (_) =>
            {
                await _semaphore.WaitAsync(token);
                if (_currentSource == source)
                    await DisconnectCurrentSourceAsync(token);
                _semaphore.Release();
            }));
        }
    }

    protected async virtual void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();

        if (_task != null)
            await _task;

        _semaphore?.Dispose();
        _currentSource?.Dispose();
        _cancellationSource?.Dispose();

        _task = null;
        _semaphore = null;
        _currentSource = null;
        _cancellationSource = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
