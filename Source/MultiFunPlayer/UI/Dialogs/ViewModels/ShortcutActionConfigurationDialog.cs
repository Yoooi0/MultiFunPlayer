using MultiFunPlayer.Shortcut;
using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class ShortcutActionConfigurationDialog(IShortcutActionConfiguration configuration) : Screen
{
    public IShortcutActionConfiguration Configuration { get; } = configuration;
    public IEnumerable<IShortcutSetting> Settings => Configuration.Settings;
}