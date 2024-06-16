using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json.Linq;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class MediaSourceViewModel : Conductor<IMediaSource>.Collection.OneActive, IHandle<SettingsMessage>, IDisposable
{
    private Task _task;
    private CancellationTokenSource _cancellationSource;
    private IMediaSource _currentSource;
    private SemaphoreSlim _semaphore;
    private SemaphoreSlim _scanIntervalSemaphore;

    public IReadOnlyList<IMediaSource> AvailableSources { get; }

    public bool ContentVisible { get; set; }
    public int ScanDelay { get; set; } = 2500;
    public int ScanInterval { get; set; } = 5000;

    public MediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IEnumerable<IMediaSource> sources)
    {
        eventAggregator.Subscribe(this);

        AvailableSources = new List<IMediaSource>(sources);

        _currentSource = null;
        _semaphore = new SemaphoreSlim(1, 1);
        _cancellationSource = new CancellationTokenSource();
        _scanIntervalSemaphore = new SemaphoreSlim(0);

        RegisterActions(shortcutManager);
    }

    public async void ToggleItem(IMediaSource source)
    {
        if (!Items.Contains(source))
        {
            Items.Add(source);
            ActiveItem = source;
        }
        else
        {
            var token = _cancellationSource.Token;
            await _semaphore.WaitAsync(token);
            if (_currentSource == source)
                await DisconnectCurrentSourceAsync(token);

            var selectedIndex = Items.IndexOf(ActiveItem);
            Items.Remove(source);
            ActiveItem = Items.Count > 0 ? Items[Math.Clamp(selectedIndex, 0, Items.Count - 1)] : null;

            _semaphore.Release();
        }

        NotifyOfPropertyChange(() => Items);
    }

    protected override void OnViewLoaded()
    {
        base.OnViewLoaded();
        _task ??= Task.Run(() => ScanAsync(_cancellationSource.Token));
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
            settings[nameof(Items)] = JArray.FromObject(Items.Select(x => x.Name));
            settings[nameof(ActiveItem)] = ActiveItem?.Name;

            foreach (var source in AvailableSources)
            {
                if (!settings.EnsureContainsObjects(source.Name)
                 || !settings.TryGetObject(out var sourceSettings, source.Name))
                    continue;

                source.HandleSettings(sourceSettings, message.Action);
            }
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
            if (settings.TryGetValue<List<string>>(nameof(Items), out var items))
                Items.AddRange(AvailableSources.Where(x => items.Exists(s => string.Equals(s, x.Name, StringComparison.OrdinalIgnoreCase))));
            if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem, StringComparison.OrdinalIgnoreCase)) ?? Items.FirstOrDefault(), closePrevious: false);

            foreach (var source in AvailableSources)
            {
                if (!settings.TryGetObject(out var sourceSettings, source.Name))
                    continue;

                source.HandleSettings(sourceSettings, message.Action);
            }
        }
    }

    public async Task ToggleConnectAsync(IMediaSource source)
    {
        var token = _cancellationSource.Token;
        await _semaphore.WaitAsync(token);
        if (_currentSource == source)
        {
            if (_currentSource?.Status == ConnectionStatus.Connected)
                await DisconnectCurrentSourceAsync(token);
            else if (_currentSource?.Status == ConnectionStatus.Disconnected)
                await ConnectAsync(_currentSource, ConnectionType.Manual, token);
        }
        else if (_currentSource != source)
        {
            await ConnectAndSetAsCurrentSourceAsync(source, token);
        }

        _semaphore.Release();
    }

    private async Task ConnectAndSetAsCurrentSourceAsync(IMediaSource source, CancellationToken token)
    {
        if (_currentSource != null)
            await DisconnectCurrentSourceAsync(token);

        if (source != null)
            await ConnectAsync(source, ConnectionType.Manual, token);

        if (source == null || source.Status == ConnectionStatus.Connected)
            _currentSource = source;
    }

    private async Task DisconnectCurrentSourceAsync(CancellationToken token)
    {
        await DisconnectAsync(_currentSource, token);
        _currentSource = null;
    }

    private async Task ConnectAsync(IMediaSource source, ConnectionType connectionType, CancellationToken token)
    {
        _scanIntervalSemaphore.Release();
        await source.ConnectAsync(connectionType);
        await source.WaitForIdle(token);
    }

    private async Task DisconnectAsync(IMediaSource source, CancellationToken token)
    {
        _scanIntervalSemaphore.Release();
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
                if (_currentSource == null)
                {
                    foreach (var source in Items.ToList())
                    {
                        if (_currentSource != null)
                            break;

                        if (!source.AutoConnectEnabled)
                            continue;

                        await _semaphore.WaitAsync(token);
                        if (_currentSource == null)
                        {
                            await ConnectAsync(source, ConnectionType.AutoConnect, token);

                            if (source.Status == ConnectionStatus.Connected)
                                _currentSource = source;
                        }

                        _semaphore.Release();
                    }
                }

                if (_currentSource != null)
                {
                    await _currentSource.WaitForDisconnect(token);
                    await _semaphore.WaitAsync(token);
                    if (_currentSource?.Status == ConnectionStatus.Disconnected)
                        _currentSource = null;
                    _semaphore.Release();
                }

                while (await _scanIntervalSemaphore.WaitAsync(ScanInterval, token));
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RegisterActions(IShortcutManager s)
    {
        var token = _cancellationSource.Token;
        foreach (var source in AvailableSources)
        {
            s.RegisterAction($"{source.Name}::Connection::Toggle", async () => await ToggleConnectAsync(source));
            s.RegisterAction($"{source.Name}::Connection::Connect", async () =>
            {
                await _semaphore.WaitAsync(token);
                if (_currentSource != source)
                    await ConnectAndSetAsCurrentSourceAsync(source, token);
                _semaphore.Release();
            });
            s.RegisterAction($"{source.Name}::Connection::Disconnect", async () =>
            {
                await _semaphore.WaitAsync(token);
                if (_currentSource == source)
                    await DisconnectCurrentSourceAsync(token);
                _semaphore.Release();
            });
        }
    }

    private void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();

        _task?.GetAwaiter().GetResult();

        _semaphore?.Dispose();
        _currentSource?.Dispose();
        _cancellationSource?.Dispose();
        _scanIntervalSemaphore?.Dispose();

        _task = null;
        _semaphore = null;
        _currentSource = null;
        _cancellationSource = null;
        _scanIntervalSemaphore = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
