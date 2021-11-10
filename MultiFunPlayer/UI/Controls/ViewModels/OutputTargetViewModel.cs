using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.OutputTarget;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
{
    private Task _task;
    private CancellationTokenSource _cancellationSource;
    private Dictionary<IOutputTarget, SemaphoreSlim> _semaphores;

    public bool ContentVisible { get; set; }

    public OutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IEnumerable<IOutputTarget> targets)
    {
        eventAggregator.Subscribe(this);
        Items.AddRange(targets);

        _semaphores = targets.ToDictionary(t => t, _ => new SemaphoreSlim(1, 1));

        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => ScanAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();

        RegisterShortcuts(shortcutManager);
    }

    public void Handle(AppSettingsMessage message)
    {
        if (message.Type == AppSettingsMessageType.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("OutputTarget")
             || !message.Settings.TryGetObject(out var settings, "OutputTarget"))
                return;

            settings[nameof(ContentVisible)] = ContentVisible;

            if (ActiveItem != null)
                settings[nameof(ActiveItem)] = ActiveItem.Name;
        }
        else if (message.Type == AppSettingsMessageType.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "OutputTarget"))
                return;

            if (settings.TryGetValue<bool>(nameof(ContentVisible), out var contentVisible))
                ContentVisible = contentVisible;

            if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem)) ?? Items[0], closePrevious: false);
        }
    }

    public async Task ToggleConnectAsync(IOutputTarget target)
    {
        var token = _cancellationSource.Token;
        if (target == null)
            return;

        await _semaphores[target].WaitAsync(token);
        if (target.Status == ConnectionStatus.Connected)
        {
            await target.DisconnectAsync();
            await target.WaitForDisconnect(token);
        }
        else if (target.Status == ConnectionStatus.Disconnected)
        {
            await target.ConnectAsync();
            await target.WaitForIdle(token);
        }

        _semaphores[target].Release();
    }

    private async Task ScanAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(2500, token);
            while (!token.IsCancellationRequested)
            {
                foreach (var target in Items.ToList())
                {
                    if (!target.AutoConnectEnabled)
                        continue;

                    await _semaphores[target].WaitAsync(token);
                    if (target.Status != ConnectionStatus.Connected && await target.CanConnectAsyncWithStatus(token))
                    {
                        await target.ConnectAsync();
                        await target.WaitForIdle(token);
                    }

                    _semaphores[target].Release();
                }

                await Task.Delay(5000, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RegisterShortcuts(IShortcutManager s)
    {
        var token = _cancellationSource.Token;
        foreach (var target in Items)
        {
            #region Connection
            s.RegisterAction($"{target.Name}::Connection::Toggle", async (_) => await ToggleConnectAsync(target));
            s.RegisterAction($"{target.Name}::Connection::Connect", async (_) =>
            {
                await _semaphores[target].WaitAsync(token);

                if (target.Status == ConnectionStatus.Disconnected)
                {
                    await target.ConnectAsync();
                    await target.WaitForIdle(token);
                }

                _semaphores[target].Release();
            });
            s.RegisterAction($"{target.Name}::Connection::Disconnect", async (_) =>
            {
                await _semaphores[target].WaitAsync(token);

                if (target.Status == ConnectionStatus.Connected)
                {
                    await target.DisconnectAsync();
                    await target.WaitForDisconnect(token);
                }

                _semaphores[target].Release();
            });
            #endregion
        }
    }

    protected async virtual void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();

        if (_task != null)
            await _task;

        if (_semaphores != null)
            foreach (var (_, semaphore) in _semaphores)
                semaphore.Dispose();

        _cancellationSource?.Dispose();

        _semaphores = null;
        _task = null;
        _cancellationSource = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
