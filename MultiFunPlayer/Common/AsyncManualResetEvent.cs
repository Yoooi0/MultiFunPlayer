using System.Threading.Tasks;
using System.Threading;

namespace MultiFunPlayer.Common
{
    public sealed class AsyncManualResetEvent
    {
        private readonly object _mutex;
        private TaskCompletionSource<object> _completionSource;

        public AsyncManualResetEvent() : this(false) { }
        public AsyncManualResetEvent(bool set)
        {
            _mutex = new object();
            _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (set)
                _completionSource.TrySetResult(null);
        }

        public bool IsSet
        {
            get { lock (_mutex) return _completionSource.Task.IsCompleted; }
        }

        public Task WaitAsync()
        {
            lock (_mutex)
            {
                return _completionSource.Task;
            }
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            var waitTask = WaitAsync();
            if (waitTask.IsCompleted)
                return waitTask;

            return waitTask.WithCancellation(cancellationToken);
        }

        public void Set()
        {
            lock (_mutex)
            {
                _completionSource.TrySetResult(null);
            }
        }

        public void Reset()
        {
            lock (_mutex)
            {
                _completionSource.TrySetResult(null);
                if (_completionSource.Task.IsCompleted)
                    _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
