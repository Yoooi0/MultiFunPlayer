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

        public virtual void Start()
        {
            Stop();

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => Stop());
        }

        public virtual void Stop()
        {
            Status = VideoSourceStatus.Disconnected;

            _cancellationSource?.Cancel();
            _task?.Wait();
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
