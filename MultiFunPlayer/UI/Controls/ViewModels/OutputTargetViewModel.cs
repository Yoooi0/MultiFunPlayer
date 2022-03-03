using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.OutputTarget;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
{
    private readonly IShortcutManager _shortcutManager;
    private readonly IOutputTargetFactory _outputTargetFactory;
    private Task _task;
    private CancellationTokenSource _cancellationSource;

    public List<Type> AvailableOutputTargetTypes { get; }

    private Dictionary<IOutputTarget, SemaphoreSlim> _semaphores;

    public bool ContentVisible { get; set; }
    public int ScanDelay { get; set; } = 2500;
    public int ScanInterval { get; set; } = 5000;

    public OutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IOutputTargetFactory outputTargetFactory)
    {
        _shortcutManager = shortcutManager;
        _outputTargetFactory = outputTargetFactory;
        eventAggregator.Subscribe(this);

        _semaphores = new Dictionary<IOutputTarget, SemaphoreSlim>();
        _cancellationSource = new CancellationTokenSource();

        AvailableOutputTargetTypes = ReflectionUtils.FindImplementations<IOutputTarget>().ToList();
    }

    public void AddItem(Type type)
    {
        var index = GetFirstFreeIndex(type);
        if (index < 0)
            return;

        var instance = _outputTargetFactory.CreateOutputTarget(type, index);
        if (instance == null)
            return;

        AddItem(instance);
    }

    private void AddItem(IOutputTarget target)
    {
        Items.Add(target);
        _semaphores.Add(target, new SemaphoreSlim(1, 1));
        ActiveItem = target;

        RegisterActions(_shortcutManager, target);
    }

    public async void RemoveItem(IOutputTarget target)
    {
        var index = Items.IndexOf(target);

        Items.Remove(target);
        var semaphore = _semaphores[target];
        _semaphores.Remove(target);

        var token = _cancellationSource.Token;
        await semaphore.WaitAsync(token);

        await target.WaitForIdle(token); 
        if (target.Status == ConnectionStatus.Connected)
        {
            await target.DisconnectAsync();
            await target.WaitForDisconnect(token);
        }

        UnregisterActions(_shortcutManager, target);

        semaphore.Release();
        semaphore.Dispose();
        target.Dispose();

        ActiveItem = Items.Count > 0 ? Items[MathUtils.Clamp(index, 0, Items.Count - 1)] : null;
    }

    public int GetFirstFreeIndex(Type type)
    {
        var used = Items.Where(x => x.GetType() == type)
                        .Select(x => x.InstanceIndex)
                        .ToList();

        for(var i = 0;; i++)
            if(!used.Contains(i))
                return i;
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

    public void Handle(AppSettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("OutputTarget")
             || !message.Settings.TryGetObject(out var settings, "OutputTarget"))
                return;

            settings[nameof(ContentVisible)] = ContentVisible;
            settings[nameof(ScanDelay)] = ScanDelay;
            settings[nameof(ScanInterval)] = ScanInterval;

            if (ActiveItem != null)
                settings[nameof(ActiveItem)] = ActiveItem.Name;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "OutputTarget"))
                return;

            if (settings.TryGetValue<bool>(nameof(ContentVisible), out var contentVisible))
                ContentVisible = contentVisible;
            if (settings.TryGetValue<int>(nameof(ScanDelay), out var scanDelay))
                ScanDelay = scanDelay;
            if (settings.TryGetValue<int>(nameof(ScanInterval), out var scanInterval))
                ScanInterval = scanInterval;

            if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem)) ?? Items.FirstOrDefault(), closePrevious: false);
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
            await Task.Delay(ScanDelay, token);
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

                await Task.Delay(ScanInterval, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RegisterActions(IShortcutManager s, IOutputTarget target)
    {
        var token = _cancellationSource.Token;
        target.RegisterActions(s);

        #region Connection
        s.RegisterAction($"{target.Identifier}::Connection::Toggle", b => b.WithCallback(async (_) => await ToggleConnectAsync(target)));
        s.RegisterAction($"{target.Identifier}::Connection::Connect", b => b.WithCallback(async (_) =>
        {
            await _semaphores[target].WaitAsync(token);

            if (target.Status == ConnectionStatus.Disconnected)
            {
                await target.ConnectAsync();
                await target.WaitForIdle(token);
            }

            _semaphores[target].Release();
        }));
        s.RegisterAction($"{target.Identifier}::Connection::Disconnect", b => b.WithCallback(async (_) =>
        {
            await _semaphores[target].WaitAsync(token);

            if (target.Status == ConnectionStatus.Connected)
            {
                await target.DisconnectAsync();
                await target.WaitForDisconnect(token);
            }

            _semaphores[target].Release();
        }));
        #endregion
    }

    private void UnregisterActions(IShortcutManager s, IOutputTarget target)
    {
        target.UnregisterActions(s);
        s.UnregisterAction($"{target.Identifier}::Connection::Toggle");
        s.UnregisterAction($"{target.Identifier}::Connection::Connect");
        s.UnregisterAction($"{target.Identifier}::Connection::Disconnect");
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
