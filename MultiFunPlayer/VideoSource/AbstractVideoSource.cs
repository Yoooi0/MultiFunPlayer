using Stylet;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public abstract class AbstractVideoSource : PropertyChangedBase, IVideoSource
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public VideoSourceStatus Status { get; protected set; }

        protected AbstractVideoSource() { }

        public abstract string Name { get; }
        protected abstract Task RunAsync(CancellationToken token);

        public async virtual Task StartAsync()
        {
            await StopAsync().ConfigureAwait(false);

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => StopAsync()).Unwrap();
        }

        public async virtual Task StopAsync()
        {
            Status = VideoSourceStatus.Disconnected;

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task.ConfigureAwait(false);

            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;
        }

        protected async virtual void Dispose(bool disposing)
        {
            await StopAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
