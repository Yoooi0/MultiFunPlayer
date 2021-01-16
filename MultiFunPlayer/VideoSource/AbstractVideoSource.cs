using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MultiFunPlayer.VideoSource
{
    public abstract class AbstractVideoSource : PropertyChangedBase, IVideoSource
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public VideoSourceStatus Status { get; protected set; } = VideoSourceStatus.Disconnected;

        protected AbstractVideoSource() { }

        public abstract string Name { get; }
        protected abstract Task RunAsync(CancellationToken token);

        public async virtual Task StartAsync()
        {
            if (Status != VideoSourceStatus.Disconnected)
                return;

            Status = VideoSourceStatus.Connecting;
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => StopAsync()).Unwrap();

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async virtual Task StopAsync()
        {
            if (Status == VideoSourceStatus.Disconnected || Status == VideoSourceStatus.Disconnecting)
                return;

            Status = VideoSourceStatus.Disconnecting;

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task.ConfigureAwait(false);

            await Task.Delay(250).ConfigureAwait(false);
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;

            Status = VideoSourceStatus.Disconnected;
        }

        public async virtual ValueTask<bool> CanStartAsync(CancellationToken token) => await ValueTask.FromResult(false).ConfigureAwait(false);
        public async Task WaitForStatus(IEnumerable<VideoSourceStatus> statuses, int checkFrequency, CancellationToken token)
        {
            if (statuses.Contains(Status))
                return;

            //TODO: not great, not terrible
            while (!statuses.Contains(Status))
                await Task.Delay(checkFrequency, token).ConfigureAwait(false);
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
