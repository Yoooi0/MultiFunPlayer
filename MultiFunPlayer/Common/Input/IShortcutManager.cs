using System;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutManager
    {
        void RegisterAction(IShortcutAction action);

        void RegisterAction(string name, Action action) => RegisterAction(new ShortcutAction(name, action));
        void RegisterAction(string name, Action<float, float> action) => RegisterAction(new AxisShortcutAction(name, action));
    }
}
