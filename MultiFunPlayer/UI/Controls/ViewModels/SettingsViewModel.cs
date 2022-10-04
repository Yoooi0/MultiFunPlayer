using Stylet;
using StyletIoC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class SettingsViewModel : Conductor<IScreen>.Collection.AllActive
{
    [Inject] public GeneralSettingsViewModel General { get; set; }
    [Inject] public ThemeSettingsViewModel Theme { get; set; }
    [Inject] public ShortcutSettingsViewModel Shortcut { get; set; }

    protected override void OnInitialActivate()
    {
        Items.Add(General);
        Items.Add(Theme);
        Items.Add(Shortcut);

        ActivateAndSetParent(Items);
        base.OnActivate();
    }
}
