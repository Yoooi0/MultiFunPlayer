using MultiFunPlayer.Common;
using Newtonsoft.Json;
using PropertyChanged;

namespace MultiFunPlayer.Input;

public interface IShortcut
{
    IInputGestureDescriptor Gesture { get; }
    ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    Type OutputDataType { get; }
    bool Enabled { get; set; }

    IInputGestureData CreateData(IInputGesture inputGesture);

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
public abstract partial class AbstractShortcut<TGesture, TData>(IInputGestureDescriptor gesture)
    : IShortcut where TGesture : IInputGesture where TData : IInputGestureData
{
    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
    public IInputGestureDescriptor Gesture { get; } = gesture;

    [JsonProperty("Actions")]
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; } = [];
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public Type OutputDataType { get; } = typeof(TData);

    protected abstract TData CreateData(TGesture gesture);

    IInputGestureData IShortcut.CreateData(IInputGesture inputGesture)
        => inputGesture is TGesture input && input.Descriptor.Equals(Gesture) ? CreateData(input) : null;
}