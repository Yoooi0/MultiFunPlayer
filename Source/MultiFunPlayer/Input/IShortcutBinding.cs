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
public partial class ShortcutBinding : IShortcutBinding
{
    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
    public IInputGestureDescriptor Gesture { get; }

    [JsonProperty("Actions")]
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    public bool Enabled { get; set; }

    public ShortcutBinding(IInputGestureDescriptor gesture)
    {
        Gesture = gesture;
        Configurations = new ObservableConcurrentCollection<IShortcutActionConfiguration>();
        Enabled = true;
    }
}