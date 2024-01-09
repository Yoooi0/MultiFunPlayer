using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcut
{
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
    private Timer _taskTimer;

    protected object SyncRoot { get; } = new();

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

    protected void ScheduleTask(int milisecondsDelay, Action action)
        => ScheduleTask(TimeSpan.FromMilliseconds(milisecondsDelay), action);
    protected void ScheduleTask(TimeSpan delay, Action action)
    {
        CancelTask();
        _taskTimer = new Timer(TimerCallback, action, delay, Timeout.InfiniteTimeSpan);
    }

    protected void CancelTask() => _taskTimer?.Dispose();

    private void TimerCallback(object state)
    {
        lock(SyncRoot)
            ((Action)state)();
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
}