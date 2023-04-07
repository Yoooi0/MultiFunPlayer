using MultiFunPlayer.Common;
using System.ComponentModel;

namespace MultiFunPlayer.Input;

public interface IShortcutBinding
{
    IInputGestureDescriptor Gesture { get; }
    ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
    bool Enabled { get; set; }
}

public class ShortcutBinding : IShortcutBinding, INotifyPropertyChanged
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

    public event PropertyChangedEventHandler PropertyChanged;
}