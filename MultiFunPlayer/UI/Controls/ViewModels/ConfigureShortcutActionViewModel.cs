using MultiFunPlayer.Input;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class ConfigureShortcutActionViewModel : Screen
{
    public IShortcutAction Action { get; }
    public IEnumerable<IShortcutSetting> Settings => Action.Settings;

    public ConfigureShortcutActionViewModel(IShortcutAction action)
    {
        Action = action;
    }
}