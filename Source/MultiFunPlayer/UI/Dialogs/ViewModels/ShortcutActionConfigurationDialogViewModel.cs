using MultiFunPlayer.Input;
using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class ShortcutActionConfigurationDialogViewModel(IShortcutActionConfiguration configuration) : Screen
{
    public IShortcutActionConfiguration Configuration { get; } = configuration;
    public IEnumerable<IShortcutSetting> Settings => Configuration.Settings;
}