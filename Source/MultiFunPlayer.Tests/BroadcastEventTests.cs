using MultiFunPlayer.Common;

namespace MultiFunPlayer.Tests;

public class BroadcastEventTests
{
    private const string _context = "context";
    private const string _value = "value";

    [Fact]
    public void RegisterContextThrowsWhenContextAlreadyRegistered()
    {
        var e = new BroadcastEvent<object>();

        e.RegisterContext(_context);
        Assert.Throws<InvalidOperationException>(() => e.RegisterContext(_context));
    }

    [Fact]
    public void UnregisterContextDoesNotThrowWithoutRegisteredContext()
    {
        var e = new BroadcastEvent<object>();
        e.UnregisterContext(_context);
    }

    [Fact]
    public void UnregisterContextDoesNotThrowWithRegisteredContext()
    {
        var e = new BroadcastEvent<object>();
        e.RegisterContext(_context);
        e.UnregisterContext(_context);
    }

    [Fact]
    public void WaitOneSucceedsWithSetValue()
    {
        var e = new BroadcastEvent<string>();
        e.RegisterContext(_context);
        e.Set(_value);

        (var success, var result) = e.WaitOne(_context, CancellationToken.None);
        Assert.True(success);
        Assert.Equal(_value, result);
    }

    [Fact]
    public async Task WaitOneAsyncSucceedsWithSetValue()
    {
        var e = new BroadcastEvent<string>();
        e.RegisterContext(_context);
        e.Set(_value);

        (var success, var result) = await e.WaitOneAsync(_context, CancellationToken.None);
        Assert.True(success);
        Assert.Equal(_value, result);
    }

    [Fact]
    public void WaitOneThrowsWithCancelledToken()
    {
        var e = new BroadcastEvent<string>();
        e.RegisterContext(_context);
        e.Set(_value);

        Assert.Throws<OperationCanceledException>(() => e.WaitOne(_context, new CancellationToken(true)));
    }

    [Fact]
    public async Task WaitOneAsyncThrowsWithCancelledToken()
    {
        var e = new BroadcastEvent<string>();
        e.RegisterContext(_context);
        e.Set(_value);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await e.WaitOneAsync(_context, new CancellationToken(true)));
    }

    [Fact]
    public void WaitAnySucceedsWithValidIndex()
    {
        const int Index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(_context);

        es[Index].Set(_value);
        var result = BroadcastEvent<string>.WaitAny(es, _context, CancellationToken.None);

        Assert.Equal(Index, result.Index);
        Assert.Equal(_value, result.Value);
    }

    [Fact]
    public async Task WaitAnyAsyncSucceedsWithValidIndex()
    {
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(_context);

        es[index].Set(_value);
        var result = await BroadcastEvent<string>.WaitAnyAsync(es, _context, CancellationToken.None);

        Assert.Equal(index, result.Index);
        Assert.Equal(_value, result.Value);
    }

    [Fact]
    public void WaitAnyThrowsWithCancelledToken()
    {
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(_context);

        es[index].Set(_value);

        Assert.Throws<OperationCanceledException>(() => BroadcastEvent<string>.WaitAny(es, _context, new CancellationToken(true)));
    }

    [Fact]
    public async Task WaitAnyAsyncThrowsWithCancelledToken()
    {
        const int index = 1;

        var es = new BroadcastEvent<string>[] { new(), new(), new() };
        foreach (var e in es)
            e.RegisterContext(_context);

        es[index].Set(_value);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await BroadcastEvent<string>.WaitAnyAsync(es, _context, new CancellationToken(true)));
    }
}
