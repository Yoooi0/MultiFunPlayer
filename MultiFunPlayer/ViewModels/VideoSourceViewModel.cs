﻿using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.VideoSource;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class VideoSourceViewModel : Conductor<IVideoSource>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
    {
        private Task _task;
        private CancellationTokenSource _cancellationSource;
        private IVideoSource _currentSource;
        private SemaphoreSlim _semaphore;

        public VideoSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IEnumerable<IVideoSource> sources)
        {
            eventAggregator.Subscribe(this);
            Items.AddRange(sources);

            _currentSource = null;

            _semaphore = new SemaphoreSlim(1, 1);
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
                if (!message.Settings.EnsureContainsObjects("VideoSource")
                 || !message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                if(ActiveItem != null)
                    settings[nameof(ActiveItem)] = ActiveItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                if (settings.TryGetValue<string>(nameof(ActiveItem), out var selectedItem))
                    ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItem)) ?? Items[0], closePrevious: false);
            }
        }

        public async Task ToggleConnectAsync(IVideoSource source)
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

        private async Task ConnectAndSetAsCurrentSourceAsync(IVideoSource source, CancellationToken token)
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

        private async Task ConnectAsync(IVideoSource source, CancellationToken token)
        {
            await source.ConnectAsync();
            await source.WaitForIdle(token);
        }

        private async Task DisconnectAsync(IVideoSource source, CancellationToken token)
        {
            await source.DisconnectAsync();
            await source.WaitForDisconnect(token);
        }

        private async Task ScanAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(2500, token);
                while (!token.IsCancellationRequested)
                {
                    if (_currentSource != null)
                    {
                        await _currentSource.WaitForDisconnect(token);
                        await _semaphore.WaitAsync(token);
                        if(_currentSource?.Status == ConnectionStatus.Disconnected)
                            _currentSource = null;
                        _semaphore.Release();
                    }

                    foreach(var source in Items.ToList())
                    {
                        if (_currentSource != null)
                            break;

                        if (!source.AutoConnectEnabled)
                            continue;

                        if(await source.CanConnectAsyncWithStatus(token))
                        {
                            await _semaphore.WaitAsync(token);
                            if(_currentSource == null)
                            {
                                await source.ConnectAsync();
                                await source.WaitForIdle(token);

                                if (source.Status == ConnectionStatus.Connected)
                                    _currentSource = source;
                            }

                            _semaphore.Release();
                        }
                    }

                    await Task.Delay(5000, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        protected override void ChangeActiveItem(IVideoSource newItem, bool closePrevious)
        {
            if(ActiveItem != null && newItem != null)
            {
                newItem.ContentVisible = ActiveItem.ContentVisible;
                ActiveItem.ContentVisible = false;
            }

            base.ChangeActiveItem(newItem, closePrevious);
        }

        private void RegisterShortcuts(IShortcutManager shortcutManger)
        {
            var token = _cancellationSource.Token;
            foreach (var source in Items)
            {
                shortcutManger.RegisterAction($"{source.Name}::Connection::Toggle", async () => await ToggleConnectAsync(source));
                shortcutManger.RegisterAction($"{source.Name}::Connection::Connect", async () =>
                {
                    await _semaphore.WaitAsync(token);
                    if (_currentSource != source)
                        await ConnectAndSetAsCurrentSourceAsync(source, token);
                    _semaphore.Release();
                });
                shortcutManger.RegisterAction($"{source.Name}::Connection::Disconnect", async () =>
                {
                    await _semaphore.WaitAsync(token);
                    if (_currentSource == source)
                        await DisconnectCurrentSourceAsync(token);
                    _semaphore.Release();
                });
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
}
