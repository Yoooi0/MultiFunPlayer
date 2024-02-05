using Stylet;
using StyletIoC;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class SettingsViewModel : Conductor<IScreen>.Collection.OneActive
{
    [Inject] public GeneralSettingsViewModel General { get; set; }
    [Inject] public DeviceSettingsViewModel Device { get; set; }
    [Inject] public ThemeSettingsViewModel Theme { get; set; }
    [Inject] public InputSettingsViewModel Input { get; set; }
    [Inject] public ShortcutSettingsViewModel Shortcut { get; set; }

    protected override void OnInitialActivate()
    {
        Items.Add(General);
        Items.Add(Device);
        Items.Add(Theme);
        Items.Add(Input);
        Items.Add(Shortcut);

        base.OnInitialActivate();
    }
}
