namespace MultiFunPlayer.Common;

internal sealed class BroadcastEvent<T> : IDisposable where T : class
{
    private ManualResetEvent _waitHandle;
    private volatile T _value;

    public BroadcastEvent(bool initialState)
    {
        _waitHandle = new ManualResetEvent(initialState);
    }

    public void Set(T value)
    {
        _value = value;
        _waitHandle.Set();
    }

    public (bool Success, T Value) WaitOne() => (_waitHandle.WaitOne() && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(CancellationToken cancellationToken) => (_waitHandle.WaitOne(cancellationToken) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, CancellationToken cancellationToken) => (_waitHandle.WaitOne(millisecondsTimeout, cancellationToken) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(TimeSpan timeout, CancellationToken cancellationToken) => (_waitHandle.WaitOne(timeout, cancellationToken) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, bool exitContext, CancellationToken cancellationToken) => (_waitHandle.WaitOne(millisecondsTimeout, exitContext, cancellationToken) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(TimeSpan timeout, bool exitContext, CancellationToken cancellationToken) => (_waitHandle.WaitOne(timeout, exitContext, cancellationToken) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(int millisecondsTimeout) => (_waitHandle.WaitOne(millisecondsTimeout) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, bool exitContext) => (_waitHandle.WaitOne(millisecondsTimeout, exitContext) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(TimeSpan timeout) => (_waitHandle.WaitOne(timeout) && _waitHandle.Reset(), _value);
    public (bool Success, T Value) WaitOne(TimeSpan timeout, bool exitContext) => (_waitHandle.WaitOne(timeout, exitContext) && _waitHandle.Reset(), _value);

    public async Task<(bool Success, T Value)> WaitOneAsync(CancellationToken cancellationToken)
    {
        if (_waitHandle.WaitOne(0, cancellationToken))
            return (_waitHandle.Reset(), _value);
        else
            return (await _waitHandle.WaitOneAsync(cancellationToken) && _waitHandle.Reset(), _value);
    }

    public static (int Index, T Value) WaitAny(BroadcastEvent<T>[] events, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandle).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles);
        cancellationToken.ThrowIfCancellationRequested();

        var signalledEvent = events[index];
        var result = (index, signalledEvent._value);
        signalledEvent._waitHandle.Reset();
        return result;
    }

    public static async Task<(int Index, T Value)> WaitAnyAsync(IList<BroadcastEvent<T>> events, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandle).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles, 0);
        cancellationToken.ThrowIfCancellationRequested();

        if (index != WaitHandle.WaitTimeout)
        {
            var signalledEvent = events[index];
            var result = (index, signalledEvent._value);
            signalledEvent._waitHandle.Reset();
            return result;
        }
        else
        {
            var tasks = events.Select(e => e.WaitOneAsync(cancellationToken)).ToList();
            var task = await Task.WhenAny(tasks);
            cancellationToken.ThrowIfCancellationRequested();

            var signalledEvent = events[tasks.IndexOf(task)];
            var result = (index, signalledEvent._value);
            signalledEvent._waitHandle.Reset();
            return result;
        }
    }

    private void Dispose(bool disposing)
    {
        _waitHandle?.Dispose();
        _waitHandle = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}