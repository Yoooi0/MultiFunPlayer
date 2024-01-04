namespace MultiFunPlayer.Common;

internal sealed class BroadcastEvent<T>(bool initialState) : IDisposable where T : class
{
    private ManualResetEvent _waitHandle = new(initialState);
    private volatile T _value;

    public void Set(T value)
    {
        _value = value;
        _waitHandle.Set();
    }

    private T Reset()
    {
        _waitHandle.Reset();
        return _value;
    }

    public (bool Success, T Value) WaitOne() => (_waitHandle.WaitOne(), Reset());
    public (bool Success, T Value) WaitOne(CancellationToken cancellationToken) => (_waitHandle.WaitOne(cancellationToken), Reset());
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, CancellationToken cancellationToken) => (_waitHandle.WaitOne(millisecondsTimeout, cancellationToken), Reset());
    public (bool Success, T Value) WaitOne(TimeSpan timeout, CancellationToken cancellationToken) => (_waitHandle.WaitOne(timeout, cancellationToken), Reset());
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, bool exitContext, CancellationToken cancellationToken) => (_waitHandle.WaitOne(millisecondsTimeout, exitContext, cancellationToken), Reset());
    public (bool Success, T Value) WaitOne(TimeSpan timeout, bool exitContext, CancellationToken cancellationToken) => (_waitHandle.WaitOne(timeout, exitContext, cancellationToken), Reset());
    public (bool Success, T Value) WaitOne(int millisecondsTimeout) => (_waitHandle.WaitOne(millisecondsTimeout), Reset());
    public (bool Success, T Value) WaitOne(int millisecondsTimeout, bool exitContext) => (_waitHandle.WaitOne(millisecondsTimeout, exitContext), Reset());
    public (bool Success, T Value) WaitOne(TimeSpan timeout) => (_waitHandle.WaitOne(timeout), Reset());
    public (bool Success, T Value) WaitOne(TimeSpan timeout, bool exitContext) => (_waitHandle.WaitOne(timeout, exitContext), Reset());

    public async ValueTask<(bool Success, T Value)> WaitOneAsync(CancellationToken cancellationToken)
    {
        if (_waitHandle.WaitOne(0, cancellationToken))
            return (true, _value);

        return (await _waitHandle.WaitOneAsync(cancellationToken), Reset());
    }

    public static (int Index, T Value) WaitAny(BroadcastEvent<T>[] events, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandle).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles);
        cancellationToken.ThrowIfCancellationRequested();

        return (index, events[index].Reset());
    }

    public static async ValueTask<(int Index, T Value)> WaitAnyAsync(BroadcastEvent<T>[] events, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandle).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles, 0);
        cancellationToken.ThrowIfCancellationRequested();

        if (index != WaitHandle.WaitTimeout)
            return (index, events[index].Reset());

        var tasks = events.Select(e => e.WaitOneAsync(cancellationToken).AsTask()).ToList();
        var task = await Task.WhenAny(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        index = tasks.IndexOf(task);
        return (index, events[index].Reset());
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