using MultiFunPlayer.Common;
using MultiFunPlayer.Input;

namespace MultiFunPlayer.OutputTarget;

public interface IOutputTarget : IConnectable, IDisposable
{
    string Name { get; }
    string Identifier { get; }
    int InstanceIndex { get; }

    void RegisterActions(IShortcutManager shortcutManager);
    void UnregisterActions(IShortcutManager shortcutManager);
}
