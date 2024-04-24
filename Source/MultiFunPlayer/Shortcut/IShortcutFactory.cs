using MultiFunPlayer.Input;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutFactory
{
    T CreateShortcut<T>(IInputGestureDescriptor gesture) where T : IShortcut;
    IShortcut CreateShortcut(Type type, IInputGestureDescriptor gesture);
}

internal sealed class ShortcutFactory(IShortcutActionRunner actionScheduler) : IShortcutFactory
{
    public T CreateShortcut<T>(IInputGestureDescriptor gesture) where T : IShortcut
        => (T)Activator.CreateInstance(typeof(T), [actionScheduler, gesture]);

    public IShortcut CreateShortcut(Type type, IInputGestureDescriptor gesture)
        => (IShortcut)Activator.CreateInstance(type, [actionScheduler, gesture]);
}