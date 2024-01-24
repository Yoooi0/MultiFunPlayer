using System.Collections.Concurrent;
using System.Diagnostics;

namespace MultiFunPlayer.Common;

internal sealed class BroadcastEvent<T> where T : class
{
    private readonly ConcurrentDictionary<object, ManualResetEvent> _waitHandles = [];
    private readonly ConcurrentDictionary<object, Task<bool>> _tasks = [];
    private volatile T _value;

    public void Set(T value)
    {
        _value = value;
        foreach (var (_, waitHandle) in _waitHandles)
            waitHandle.Set();
    }

    public void RegisterContext(object context)
    {
        if (!_waitHandles.TryAdd(context, new ManualResetEvent(false)))
            throw new InvalidOperationException("Context can only be registered once");
    }

    public void UnregisterContext(object context)
    {
        if (_waitHandles.TryRemove(context, out var waitHandle))
            waitHandle.Dispose();
        _ = _tasks.TryRemove(context, out var _);
    }

    public (bool Success, T Value) WaitOne(object context, CancellationToken cancellationToken)
    {
        var waitHandle = _waitHandles[context];
        if (!waitHandle.WaitOne(cancellationToken))
            return (false, default);

        cancellationToken.ThrowIfCancellationRequested();
        return (true, CleanupAndGetValue(context));
    }

    public async ValueTask<(bool Success, T Value)> WaitOneAsync(object context, CancellationToken cancellationToken)
    {
        var waitHandle = _waitHandles[context];
        if (waitHandle.WaitOne(0))
            return (true, CleanupAndGetValue(context));

        if (!await GetWaitHandleTask(context, cancellationToken))
            return (false, default);

        cancellationToken.ThrowIfCancellationRequested();
        return (true, CleanupAndGetValue(context));
    }

    private T CleanupAndGetValue(object context)
    {
        if (!_waitHandles.TryRemove(context, out var waitHandle))
            throw new UnreachableException();

        _ = _tasks.TryRemove(context, out var _);
        waitHandle.Dispose();

        if (!_waitHandles.TryAdd(context, new ManualResetEvent(false)))
            throw new UnreachableException();

        return _value;
    }

    private Task<bool> GetWaitHandleTask(object context, CancellationToken cancellationToken)
        => _tasks.GetOrAdd(context, c => _waitHandles[c].WaitOneAsync(cancellationToken));

    public static (int Index, T Value) WaitAny(BroadcastEvent<T>[] events, object context, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandles[context]).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles);

        cancellationToken.ThrowIfCancellationRequested();
        return (index, events[index].CleanupAndGetValue(context));
    }

    public static async ValueTask<(int Index, T Value)> WaitAnyAsync(BroadcastEvent<T>[] events, object context, CancellationToken cancellationToken)
    {
        var waitHandles = events.Select(e => e._waitHandles[context]).Append(cancellationToken.WaitHandle).ToArray();
        var index = WaitHandle.WaitAny(waitHandles, 0);
        if (index == WaitHandle.WaitTimeout)
        {
            var tasks = events.Select(e => e.GetWaitHandleTask(context, cancellationToken)).ToList();
            var task = await Task.WhenAny(tasks);
            index = tasks.IndexOf(task);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (index, events[index].CleanupAndGetValue(context));
    }
}