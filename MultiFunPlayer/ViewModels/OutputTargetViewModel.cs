using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.OutputTarget;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MultiFunPlayer.ViewModels
{
    public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
    {
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

        protected override void ChangeActiveItem(IOutputTarget newItem, bool closePrevious)
        {
            if (ActiveItem != null && newItem != null)
            {
                newItem.ContentVisible = ActiveItem.ContentVisible;
                ActiveItem.ContentVisible = false;
            }

            base.ChangeActiveItem(newItem, closePrevious);
        }

        protected virtual void Dispose(bool disposing)
        {
            _cancellationSource?.Cancel();

            _semaphore?.Dispose();
            _currentTarget?.Dispose();
            _cancellationSource?.Dispose();

            _semaphore = null;
            _currentTarget = null;
            _cancellationSource = null;
        }

        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
