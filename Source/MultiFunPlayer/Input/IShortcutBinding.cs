using MultiFunPlayer.Common;
using PropertyChanged;
using System.ComponentModel;

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
    public IInputGestureDescriptor Gesture { get; }
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    public bool Enabled { get; set; }

    public ShortcutBinding(IInputGestureDescriptor gesture)
    {
        Gesture = gesture;
        Configurations = new ObservableConcurrentCollection<IShortcutActionConfiguration>();
        Enabled = true;
    }
}