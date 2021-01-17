using MultiFunPlayer.VideoSource;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class VideoSourceViewModel : Screen, IDisposable
    {
        private Task _task;
        private CancellationTokenSource _cancellationSource;
        private IVideoSource _currentSource;
        private SemaphoreSlim _semaphore;

        public List<IVideoSource> Sources { get; }

        public VideoSourceViewModel(IEnumerable<IVideoSource> sources)
        {
            Sources = sources.ToList();
            _currentSource = null;

            _semaphore = new SemaphoreSlim(1, 1);
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => ScanAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
        }

        public async void OnSourceClick(object sender, RoutedEventArgs e)
        {
            var source = (sender as FrameworkElement)?.DataContext as IVideoSource;

            await _semaphore.WaitAsync(_cancellationSource.Token).ConfigureAwait(false);
            if (_currentSource == source)
            {
                if (_currentSource?.Status == VideoSourceStatus.Connected)
                {
                    await _currentSource.StopAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                    _currentSource = null;
                }
                else if(_currentSource?.Status == VideoSourceStatus.Disconnected)
                {
                    await _currentSource.StartAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Connected, VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                }
            }
            else if (_currentSource != source)
            {
                if (_currentSource != null)
                {
                    await _currentSource.StopAsync().ConfigureAwait(false);
                    await _currentSource.WaitForStatus(new[] { VideoSourceStatus.Disconnected }, 100, _cancellationSource.Token).ConfigureAwait(false);
                    _currentSource = null;
                }

                if (source != null)
                {
                    await source.StartAsync().ConfigureAwait(false);
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
                        _currentSource = null;
                    }

                    foreach(var source in Sources)
                    {
                        if (_currentSource != null)
                            break;

                        if(await source.CanStartAsync(_cancellationSource.Token).ConfigureAwait(false))
                        {
                            await _semaphore.WaitAsync(_cancellationSource.Token).ConfigureAwait(false);
                            if(_currentSource == null)
                            {
                                await source.StartAsync().ConfigureAwait(false);
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
