using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.OutputTarget;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
    {
        private Task _task;
        private CancellationTokenSource _cancellationSource;
        private IOutputTarget _currentTarget;
        private SemaphoreSlim _semaphore;

        public OutputTargetViewModel(IEventAggregator eventAggregator, IEnumerable<IOutputTarget> targets)
        {
            eventAggregator.Subscribe(this);
            Items.AddRange(targets);

            _currentTarget = null;

            _semaphore = new SemaphoreSlim(1, 1);
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => ScanAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("OutputTarget")
                 || !message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                if(ActiveItem != null)
                    settings[nameof(ActiveItem)] = ActiveItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                    ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem)) ?? Items[0], closePrevious: false);
            }
        }

        public async void ToggleConnectAsync(IOutputTarget target)
        {
            var token = _cancellationSource.Token;
            await _semaphore.WaitAsync(token);
            if (_currentTarget == target)
            {
                if (_currentTarget?.Status == ConnectionStatus.Connected)
                {
                    await _currentTarget.DisconnectAsync();
                    await _currentTarget.WaitForDisconnect(token);
                    _currentTarget = null;
                }
                else if (_currentTarget?.Status == ConnectionStatus.Disconnected)
                {
                    await _currentTarget.ConnectAsync();
                    await target.WaitForIdle(token);
                }
            }
            else if (_currentTarget != target)
            {
                if (_currentTarget != null)
                {
                    await _currentTarget.DisconnectAsync();
                    await _currentTarget.WaitForDisconnect(token);
                    _currentTarget = null;
                }

                if (target != null)
                {
                    await target.ConnectAsync();
                    await target.WaitForIdle(token);
                }

                if (target == null || target.Status == ConnectionStatus.Connected)
                    _currentTarget = target;
            }

            _semaphore.Release();
        }

        private async Task ScanAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(2500, token);
                while (!token.IsCancellationRequested)
                {
                    if (_currentTarget != null)
                    {
                        await _currentTarget.WaitForDisconnect(token);
                        await _semaphore.WaitAsync(token);
                        if (_currentTarget?.Status == ConnectionStatus.Disconnected)
                            _currentTarget = null;
                        _semaphore.Release();
                    }

                    foreach (var source in Items.ToList())
                    {
                        if (_currentTarget != null)
                            break;

                        if (!source.AutoConnectEnabled)
                            continue;

                        if (await source.CanConnectAsyncWithStatus(token))
                        {
                            await _semaphore.WaitAsync(token);
                            if (_currentTarget == null)
                            {
                                await source.ConnectAsync();
                                await source.WaitForIdle(token);

                                if (source.Status == ConnectionStatus.Connected)
                                    _currentTarget = source;
                            }
                            _semaphore.Release();
                        }
                    }

                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        protected override void ChangeActiveItem(IOutputTarget newItem, bool closePrevious)
        {
            if (ActiveItem != null && newItem != null)
            {
                newItem.ContentVisible = ActiveItem.ContentVisible;
                ActiveItem.ContentVisible = false;
            }

            base.ChangeActiveItem(newItem, closePrevious);
        }

        protected async virtual void Dispose(bool disposing)
        {
            _cancellationSource?.Cancel();

            if (_task != null)
                await _task;

            _semaphore?.Dispose();
            _currentTarget?.Dispose();
            _cancellationSource?.Dispose();

            _task = null;
            _semaphore = null;
            _currentTarget = null;
            _cancellationSource = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
