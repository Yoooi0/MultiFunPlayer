using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcut : IDisposable
{
    string Name { get; }
    IInputGestureDescriptor Gesture { get; }
    ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    Type OutputDataType { get; }
    bool Enabled { get; set; }

    void Update(IInputGesture inputGesture);

    static bool AcceptsGesture(Type shortcutType, IInputGesture gesture)
    {
        if (!shortcutType.IsAssignableTo(typeof(IShortcut)))
            return false;

        var baseShortcutType = shortcutType;
        while(!baseShortcutType.IsAbstract)
            baseShortcutType = baseShortcutType.BaseType;

        var shortcutGestureType = baseShortcutType.GetGenericArguments()[0];
        return gesture.GetType().IsAssignableTo(shortcutGestureType);
    }
}

[AddINotifyPropertyChangedInterface]
internal abstract partial class AbstractShortcut<TGesture, TData>(IShortcutActionResolver actionResolver, IInputGestureDescriptor gesture)
    : IShortcut where TGesture : IInputGesture where TData : IInputGestureData
{
    private readonly ConcurrentDictionary<string, Timer> _actions = [];
    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource CancellationSource)> _repeatingActions = [];

    [JsonIgnore]
    protected object SyncRoot { get; } = new();

    public string Name { get; set; } = null;

    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
    public IInputGestureDescriptor Gesture { get; } = gesture;

    [JsonProperty("Actions")]
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; } = [];
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public Type OutputDataType { get; } = typeof(TData);

    protected void Invoke(TData gestureData)
    {
        if (Configurations.Count == 0)
            return;

        foreach (var configuration in Configurations)
            if (actionResolver.TryGetAction(configuration.Name, out var action))
                action.Invoke(configuration, gestureData);
    }

    protected void Delay(int milisecondsDelay, Action action, string key = "")
        => Delay(TimeSpan.FromMilliseconds(milisecondsDelay), action);
    protected void Delay(TimeSpan delay, Action action, string key = "")
    {
        CancelDelay(key);
        var timer = new Timer(DelayCallback, action, delay, Timeout.InfiniteTimeSpan);
        _actions.TryAdd(key, timer);
    }

    protected void Repeat(int milisecondsPeriod, Action action, string key = "")
        => Repeat(TimeSpan.FromMilliseconds(milisecondsPeriod), action);
    protected void Repeat(TimeSpan period, Action action, string key = "")
    {
        CancelRepeat(key);
        var cancellationSource = new CancellationTokenSource();
        var token = cancellationSource.Token;
        var task = Task.Run(() => RepeatCallback(period, action, token));
        _repeatingActions.TryAdd(key, (task, cancellationSource));
    }

    protected void CancelRepeat(string key = "")
    {
        if (!_repeatingActions.TryRemove(key, out var item))
            return;

        (_, var cancellationSource) = item;
        cancellationSource.Cancel();
        cancellationSource.Dispose();
    }

    protected void CancelDelay(string key = "")
    {
        if (_actions.TryRemove(key, out var timer))
            timer?.Dispose();
    }

    protected bool IsDelayPending(string key = "") => _actions.ContainsKey(key);
    protected bool IsRepeatRunning(string key = "") => _repeatingActions.ContainsKey(key);

    private void DelayCallback(object state)
    {
        lock(SyncRoot)
            ((Action)state)();
    }

    private async Task RepeatCallback(TimeSpan period, Action action, CancellationToken token)
    {
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(token))
        {
            lock (SyncRoot)
                action();
        }
    }

    protected abstract void Update(TGesture gesture);

    void IShortcut.Update(IInputGesture gesture)
    {
        if (Enabled && gesture is TGesture input && input.Descriptor.Equals(Gesture))
        {
            lock(SyncRoot)
                Update(input);
        }
    }

    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName);
        stringBuilder.Append(" [");
        PrintMembers(stringBuilder);

        stringBuilder.Length -= 2;
        stringBuilder.Append(']');
        return stringBuilder.ToString();
    }

    protected virtual void PrintMembers(StringBuilder builder)
    {
        PrintProperty(builder, () => Gesture);
    }

    protected void PrintProperty<T>(StringBuilder builder, Expression<Func<T>> property)
    {
        builder.Append(property.NameForProperty());
        builder.Append(": ");
        builder.Append(property.Compile()());
        builder.Append(", ");
    }

    protected virtual void Dispose(bool disposing)
    {
        foreach (var (key, _) in _actions)
            CancelDelay(key);
        foreach (var (key, _) in _repeatingActions)
            CancelRepeat(key);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}