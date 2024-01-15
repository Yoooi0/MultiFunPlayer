namespace MultiFunPlayer.Common;

internal sealed class BroadcastEvent<T> where T : class
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<object, ManualResetEvent> _waitHandles = [];
    private volatile T _value;

    public void Set(T value)
    {
        lock (_syncRoot)
        {
            _value = value;
            foreach (var (_, waitHandle) in _waitHandles)
                waitHandle.Set();
        }
    }

    public void RegisterContext(object context)
    {
        lock (_syncRoot)
        {
            if (_waitHandles.ContainsKey(context))
                throw new InvalidOperationException("Context can only be registered once");
            _waitHandles[context] = new ManualResetEvent(false);
        }
    }

    public void UnregisterContext(object context)
    {
        lock (_syncRoot)
            if (_waitHandles.Remove(context, out var waitHandle))
                waitHandle.Dispose();
    }

    public (bool Success, T Value) WaitOne(object context, CancellationToken cancellationToken)
    {
        var waitHandle = GetWaitHandle(context);
        if (!waitHandle.WaitOne(cancellationToken))
            return (false, default);

        cancellationToken.ThrowIfCancellationRequested();
        return (true, GetValueAndUpdateWaitHandle(context));
    }

    public async ValueTask<(bool Success, T Value)> WaitOneAsync(object context, CancellationToken cancellationToken)
    {
        var waitHandle = GetWaitHandle(context);
        if (waitHandle.WaitOne(0))
            return (true, GetValueAndUpdateWaitHandle(context));

        if (!await waitHandle.WaitOneAsync(cancellationToken))
            return (false, default);

        cancellationToken.ThrowIfCancellationRequested();
        return (true, GetValueAndUpdateWaitHandle(context));
    }

    private ManualResetEvent GetWaitHandle(object context)
    {
        lock (_syncRoot)
            return _waitHandles[context];
    }

    private T GetValueAndUpdateWaitHandle(object context)
    {
        lock (_syncRoot)
        {
            _waitHandles[context].Dispose();
            _waitHandles[context] = new ManualResetEvent(false);
            return _value;
        }
    }

    public static (int Index, T Value) WaitAny(BroadcastEvent<T>[] events, object context, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(p => p.GetWaitHandle(context)).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles);

        cancellationToken.ThrowIfCancellationRequested();
        return (index, events[index].GetValueAndUpdateWaitHandle(context));
    }

    public static async ValueTask<(int Index, T Value)> WaitAnyAsync(BroadcastEvent<T>[] events, object context, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(p => p.GetWaitHandle(context)).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles, 0);
        if (index == WaitHandle.WaitTimeout)
        {
            using var tasksCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasksCancellationToken = tasksCancellationSource.Token;
            var tasks = waitHandles[..^1].Select(h => h.WaitOneAsync(tasksCancellationToken)).ToList();
            var task = await Task.WhenAny(tasks);
            tasksCancellationToken.ThrowIfCancellationRequested();
            tasksCancellationSource.Cancel();

            index = tasks.IndexOf(task);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (index, events[index].GetValueAndUpdateWaitHandle(context));
    }
}