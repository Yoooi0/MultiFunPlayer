using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.OutputTarget;

public interface IOutputTarget : IConnectable, IDisposable
{
    string Name { get; }
    string Identifier { get; }
    int InstanceIndex { get; }

    void HandleSettings(JObject settings, SettingsAction action);

    void RegisterActions(IShortcutManager shortcutManager);
    void UnregisterActions(IShortcutManager shortcutManager);
}
