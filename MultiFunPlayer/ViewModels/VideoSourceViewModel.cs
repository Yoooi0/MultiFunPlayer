using MultiFunPlayer.VideoSource;
using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class VideoSourceViewModel : Screen, IDisposable
    {
        private IVideoSource _currentSource;

        public List<IVideoSource> Sources { get; }

        public VideoSourceViewModel(IEnumerable<IVideoSource> sources)
        {
            Sources = sources.ToList();
            _currentSource = null;
        }

        public async void OnSourceClick(object sender, RoutedEventArgs e)
        {
            var source = (sender as FrameworkElement)?.DataContext as IVideoSource;
            if (_currentSource == source)
            {
                if (_currentSource == null)
                    return;

                if(_currentSource.Status == VideoSourceStatus.Connected)
                    await _currentSource.StopAsync().ConfigureAwait(false);
                else if (_currentSource.Status == VideoSourceStatus.Disconnected)
                    await _currentSource.StartAsync().ConfigureAwait(false);
            }
            else if (_currentSource != source)
            {
                if(_currentSource != null)
                    await _currentSource.StopAsync().ConfigureAwait(false);
                if(source != null)
                    await source.StartAsync().ConfigureAwait(false);
                _currentSource = source;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            _currentSource?.Dispose();
            _currentSource = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
