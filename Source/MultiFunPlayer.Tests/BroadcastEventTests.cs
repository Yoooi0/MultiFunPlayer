using MultiFunPlayer.Common;
using System.Collections.Concurrent;

namespace MultiFunPlayer.Tests;

public class BroadcastEventTests
{
    [Fact]
    public void RegisterContextThrowsWhenContextAlreadyRegistered()
    {
        const string context = "context";
        var e = new BroadcastEvent<object>();

        e.RegisterContext(context);
        Assert.Throws<InvalidOperationException>(() => e.RegisterContext(context));
    }

    [Fact]
    public void UnregisterContextDoesNotThrowWithoutRegisteredContext()
    {
        const string context = "context";
        var e = new BroadcastEvent<object>();
        e.UnregisterContext(context);
    }

    [Fact]
    public void UnregisterContextDoesNotThrowWithRegisteredContext()
    {
        const string context = "context";
        var e = new BroadcastEvent<object>();
        e.RegisterContext(context);
        e.UnregisterContext(context);
    }

    [Fact]
    public void WaitOneSucceedsWithSetValue()
    {
        const string context = "context";
        const string value = "value";
        var e = new BroadcastEvent<string>();
        e.RegisterContext(context);
        e.Set(value);

        (var success, var result) = e.WaitOne(context, CancellationToken.None);
        Assert.True(success);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task WaitOneAsyncSucceedsWithSetValue()
    {
        const string context = "context";
        const string value = "value";
        var e = new BroadcastEvent<string>();
        e.RegisterContext(context);
        e.Set(value);

        (var success, var result) = await e.WaitOneAsync(context, CancellationToken.None);
        Assert.True(success);
        Assert.Equal(value, result);
    }

    [Fact]
    public void WaitOneThrowsWithCancelledToken()
    {
        const string context = "context";
        const string value = "value";
        var e = new BroadcastEvent<string>();
        e.RegisterContext(context);
        e.Set(value);

        Assert.Throws<OperationCanceledException>(() => e.WaitOne(context, new CancellationToken(true)));
    }

    [Fact]
    public async Task WaitOneAsyncThrowsWithCancelledToken()
    {
        const string context = "context";
        const string value = "value";
        var e = new BroadcastEvent<string>();
        e.RegisterContext(context);
        e.Set(value);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await e.WaitOneAsync(context, new CancellationToken(true)));
    }

    [Fact]
    public void MultithreadedWaitOneAllSucceedWithSetValue()
    {
        const string value = "value";
        var e = new BroadcastEvent<string>();

        var threads = new List<Thread>();
        var results = new ConcurrentDictionary<object, (bool Success, string Value)>();
        for (var i = 0; i < 3; i++)
        {
            var context = $"context{i}";
            var thread = new Thread(() =>
            {
                e.RegisterContext(context);
                var result = e.WaitOne(context, CancellationToken.None);
                results.TryAdd(context, result);
            }) { IsBackground = true };

            threads.Add(thread);
            thread.Start();
        }

        e.Set(value);
        foreach (var thread in threads)
            thread.Join();

        foreach (var (_, result) in results)
        {
            Assert.True(result.Success);
            Assert.Equal(value, result.Value);
        }
    }

    [Fact]
    public async Task MultitaskWaitOneAsyncAllSucceedWithSetValue()
    {
        const string value = "value";
        var e = new BroadcastEvent<string>();

        var tasks = new List<Task<(bool Success, string Value)>>();
        for (var i = 0; i < 3; i++)
        {
            var context = $"context{i}";
            var task = Task.Run(async () =>
            {
                e.RegisterContext(context);
                return await e.WaitOneAsync(context, CancellationToken.None);
            });

            tasks.Add(task);
        }

        _ = Task.WhenAll(tasks);
        while (tasks.Any(t => t.Status == TaskStatus.Created))
            await Task.Delay(100);

        e.Set(value);
        await Task.WhenAll(tasks);

        foreach(var task in tasks)
        {
            var result = await task;
            Assert.True(result.Success);
            Assert.Equal(value, result.Value);
        }
    }

    [Fact]
    public void WaitAnySucceedsWithValidIndex()
    {
        const string context = "context";
        const string value = "value";
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(context);

        es[index].Set(value);
        var result = BroadcastEvent<string>.WaitAny(es, context, CancellationToken.None);

        Assert.Equal(index, result.Index);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public async Task WaitAnyAsyncSucceedsWithValidIndex()
    {
        const string context = "context";
        const string value = "value";
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(context);

        es[index].Set(value);
        var result = await BroadcastEvent<string>.WaitAnyAsync(es, context, CancellationToken.None);

        Assert.Equal(index, result.Index);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void WaitAnyThrowsWithCancelledToken()
    {
        const string context = "context";
        const string value = "value";
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(context);

        es[index].Set(value);

        Assert.Throws<OperationCanceledException>(() => BroadcastEvent<string>.WaitAny(es, context, new CancellationToken(true)));
    }

    [Fact]
    public async Task WaitAnyAsyncThrowsWithCancelledToken()
    {
        const string context = "context";
        const string value = "value";
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(context);

        es[index].Set(value);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await BroadcastEvent<string>.WaitAnyAsync(es, context, new CancellationToken(true)));
    }
}
