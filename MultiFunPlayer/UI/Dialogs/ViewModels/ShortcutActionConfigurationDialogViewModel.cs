using MultiFunPlayer.Input;
using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal class ShortcutActionConfigurationDialogViewModel : Screen
{
    public IShortcutActionConfiguration Configuration { get; }
    public IEnumerable<IShortcutSetting> Settings => Configuration.Settings;

    public ShortcutActionConfigurationDialogViewModel(IShortcutActionConfiguration configuration)
    {
        Configuration = configuration;
    }
}