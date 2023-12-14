using MultiFunPlayer.Common;
using Newtonsoft.Json;
using PropertyChanged;

namespace MultiFunPlayer.Input;

public interface IShortcutBinding
{
    IInputGestureDescriptor Gesture { get; }
    ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    bool Enabled { get; set; }
}

[AddINotifyPropertyChangedInterface]
public sealed partial class ShortcutBinding(IInputGestureDescriptor gesture) : IShortcutBinding
{
    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
    public IInputGestureDescriptor Gesture { get; } = gesture;

    [JsonProperty("Actions")]
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; } = [];
    public bool Enabled { get; set; } = true;
}