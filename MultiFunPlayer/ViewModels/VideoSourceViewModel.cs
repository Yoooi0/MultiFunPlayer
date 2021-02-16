using MultiFunPlayer.Common;
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
    public class VideoSourceViewModel : Conductor<IVideoSource>.Collection.AllActive, IHandle<AppSettingsMessage>, IDisposable
    {
        private Task _task;
        private CancellationTokenSource _cancellationSource;
        private IVideoSource _currentSource;
        private SemaphoreSlim _semaphore;

        public IVideoSource SelectedItem { get; set; }

        public VideoSourceViewModel(IEventAggregator eventAggregator, IEnumerable<IVideoSource> sources)
        {
            eventAggregator.Subscribe(this);
            foreach (var source in sources)
                Items.Add(source);

            _currentSource = null;

            _semaphore = new SemaphoreSlim(1, 1);
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => ScanAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
        }

        protected override void OnActivate()
        {
            ActivateAndSetParent(Items);
            base.OnActivate();
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("VideoSource")
                 || !message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                settings[nameof(SelectedItem)] = SelectedItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "VideoSource"))
                    return;

                if (settings.TryGetValue(nameof(SelectedItem), out var selectedItemToken))
                    SelectedItem = Items.FirstOrDefault(x => string.Equals(x.Name, selectedItemToken.ToObject<string>())) ?? Items.First();
            }
        }

        public async void ToggleConnectAsync(IVideoSource source)
        {
            await _semaphore.WaitAsync(_cancellationSource.Token).ConfigureAwait(false);
            if (_currentSource == source)
            {
                if (_currentSource?.Status == VideoSourceStatus.Connected)
                {
                    await _currentSource.DisconnectAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                    _currentSource = null;
                }
                else if(_currentSource?.Status == VideoSourceStatus.Disconnected)
                {
                    await _currentSource.ConnectAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Connected, VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                }
            }
            else if (_currentSource != source)
            {
                if (_currentSource != null)
                {
                    await _currentSource.DisconnectAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                    _currentSource = null;
                }

                if (source != null)
                {
                    await source.ConnectAsync().ConfigureAwait(false);
                    await source.WaitForStatus(new[] { VideoSourceStatus.Connected, VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                }

                if(source == null || source.Status == VideoSourceStatus.Connected)
                    _currentSource = source;
            }

            _semaphore.Release();
        }

        private async Task ScanAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(2500, token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    if (_currentSource != null)
                    {
                        await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Disconnected }, 5000, _cancellationSource.Token).ConfigureAwait(false);
                        await _semaphore.WaitAsync(_cancellationSource.Token).ConfigureAwait(false);
                        if(_currentSource?.Status == VideoSourceStatus.Disconnected)
                            _currentSource = null;
                        _semaphore.Release();
                    }

                    foreach(var source in Items)
                    {
                        if (_currentSource != null)
                            break;

                        if(await source.CanConnectAsync(_cancellationSource.Token).ConfigureAwait(false))
                        {
                            await _semaphore.WaitAsync(_cancellationSource.Token).ConfigureAwait(false);
                            if(_currentSource == null)
                            {
                                await source.ConnectAsync().ConfigureAwait(false);
                                await source.WaitForStatus(new[] { VideoSourceStatus.Connected, VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);

                                if (source.Status == VideoSourceStatus.Connected)
                                    _currentSource = source;
                            }
                            _semaphore.Release();
                        }
                    }

                    await Task.Delay(1000, _cancellationSource.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        protected async virtual void Dispose(bool disposing)
        {
            _cancellationSource?.Cancel();

            if (_task != null)
                await _task.ConfigureAwait(false);

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
