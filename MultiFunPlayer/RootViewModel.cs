using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using Stylet;
using StyletIoC;
using System.Windows;
using System.Windows.Input;

namespace MultiFunPlayer;

public class RootViewModel : Conductor<IScreen>.Collection.AllActive, IHandle<AppSettingsMessage>
{
    [Inject] public ScriptViewModel Script { get; set; }
    [Inject] public MediaSourceViewModel MediaSource { get; set; }
    [Inject] public OutputTargetViewModel OutputTarget { get; set; }
    [Inject] public ShortcutViewModel Shortcut { get; set; }
    [Inject] public ApplicationViewModel Application { get; set; }
    [Inject] public ThemeViewModel Theme { get; set; }

    public bool DisablePopup { get; set; }

    public RootViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);
    }

    protected override void OnActivate()
    {
        Items.Add(Script);
        Items.Add(MediaSource);
        Items.Add(OutputTarget);

        ActivateAndSetParent(Items);
        base.OnActivate();
    }

    public void OnInformationClick() => _ = DialogHelper.ShowOnUIThreadAsync(new InformationMessageDialogViewModel(showCheckbox: false), "RootDialog");
    public void OnShortcutClick() => _ = DialogHelper.ShowOnUIThreadAsync(Shortcut, "RootDialog");
    public void OnSettingsClick() => _ = DialogHelper.ShowOnUIThreadAsync(Application, "RootDialog");
    public void OnThemeClick() => _ = DialogHelper.ShowOnUIThreadAsync(Theme, "RootDialog");

    public void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        window.DragMove();
    }

    public void Handle(AppSettingsMessage message)
    {
        var settings = message.Settings;

        if (message.Action == SettingsAction.Saving)
        {
            settings[nameof(DisablePopup)] = DisablePopup;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            DisablePopup = settings.TryGetValue(nameof(DisablePopup), out var disablePopupToken) && disablePopupToken.Value<bool>();
            if (!DisablePopup)
            {
                Execute.PostToUIThread(async () =>
                {
                    var result = await DialogHelper.ShowAsync(new InformationMessageDialogViewModel(showCheckbox: true), "RootDialog").ConfigureAwait(true);
                    if (result is not bool disablePopup)
                        return;

                    DisablePopup = disablePopup;
                });
            }
        }
    }
}
