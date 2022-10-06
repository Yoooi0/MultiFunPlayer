using MultiFunPlayer.Input;
using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

public class ConfigureShortcutActionDialogViewModel : Screen
{
    public IShortcutAction Action { get; }
    public IEnumerable<IShortcutSetting> Settings => Action.Settings;

    public ConfigureShortcutActionDialogViewModel(IShortcutAction action)
    {
        Action = action;
    }
}