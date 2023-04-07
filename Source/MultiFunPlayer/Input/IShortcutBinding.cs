using MultiFunPlayer.Common;

namespace MultiFunPlayer.Input;

public interface IShortcutBinding
{
    IInputGestureDescriptor Gesture { get; }
    ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }
}

public class ShortcutBinding : IShortcutBinding
{
    public IInputGestureDescriptor Gesture { get; }
    public ObservableConcurrentCollection<IShortcutActionConfiguration> Configurations { get; }

    public ShortcutBinding(IInputGestureDescriptor gesture)
    {
        Gesture = gesture;
        Configurations = new ObservableConcurrentCollection<IShortcutActionConfiguration>();
    }
}