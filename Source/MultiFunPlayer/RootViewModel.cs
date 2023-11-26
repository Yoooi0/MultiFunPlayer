using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json.Linq;
using Stylet;
using StyletIoC;
using System.Windows;
using System.Windows.Input;

namespace MultiFunPlayer;

internal sealed class RootViewModel : Conductor<IScreen>.Collection.AllActive, IHandle<SettingsMessage>
{
    [Inject] public ScriptViewModel Script { get; set; }
    [Inject] public MediaSourceViewModel MediaSource { get; set; }
    [Inject] public OutputTargetViewModel OutputTarget { get; set; }
    [Inject] public SettingsViewModel Settings { get; set; }
    [Inject] public PluginViewModel Plugin { get; set; }

    public bool DisablePopup { get; set; }
    public int WindowHeight { get; set; }

    public RootViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);
    }

    protected override void OnActivate()
    {
        Items.Add(Script);
        Items.Add(MediaSource);
        Items.Add(OutputTarget);
        Items.Add(Settings);
        Items.Add(Plugin);

        ActivateAndSetParent(Items);
        base.OnActivate();
    }

    public void OnInformationClick() => _ = DialogHelper.ShowOnUIThreadAsync(new InformationMessageDialogViewModel(showCheckbox: false), "RootDialog");
    public void OnSettingsClick() => _ = DialogHelper.ShowOnUIThreadAsync(Settings, "RootDialog");
    public void OnPluginClick() => _ = DialogHelper.ShowOnUIThreadAsync(Plugin, "RootDialog");

    public void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        window.DragMove();
    }

    public void Handle(SettingsMessage message)
    {
        var settings = message.Settings;

        if (message.Action == SettingsAction.Saving)
        {
            settings[nameof(DisablePopup)] = DisablePopup;
            settings[nameof(WindowHeight)] = WindowHeight;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<int>(nameof(WindowHeight), out var windowHeight))
                WindowHeight = windowHeight;

            DisablePopup = settings.TryGetValue(nameof(DisablePopup), out var disablePopupToken) && disablePopupToken.Value<bool>();
            if (!DisablePopup)
            {
                Execute.PostToUIThread(async () =>
                {
                    var result = await DialogHelper.ShowAsync(new InformationMessageDialogViewModel(showCheckbox: true), "RootDialog");
                    if (result is not bool disablePopup)
                        return;

                    DisablePopup = disablePopup;
                });
            }
        }
    }
}
