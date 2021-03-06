using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using StyletIoC;
using System;
using System.Windows;
using System.Windows.Input;

namespace MultiFunPlayer.ViewModels
{
    public class RootViewModel : Conductor<IScreen>.Collection.AllActive
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IViewManager _viewManager;
        private readonly IEventAggregator _eventAggregator;

        [Inject] public ScriptViewModel Script { get; set; }
        [Inject] public VideoSourceViewModel VideoSource { get; set; }
        [Inject] public OutputTargetViewModel OutputTarget { get; set; }
        [Inject] public ShortcutViewModel Shortcut { get; set; }

        public RootViewModel(IViewManager viewManager, IEventAggregator eventAggregator)
        {
            _viewManager = viewManager;
            _eventAggregator = eventAggregator;
        }

        protected override void OnActivate()
        {
            Items.Add(Script);
            Items.Add(VideoSource);
            Items.Add(OutputTarget);

            ActivateAndSetParent(Items);
            base.OnActivate();

            var settings = Settings.Read();
            _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Loading));
        }

        public void OnInformationClick()
            => _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new InformationMessageDialog(showCheckbox: false), "RootDialog"));

        public void OnShortcutClick()
            => _ = Execute.OnUIThreadAsync(() => DialogHost.Show(_viewManager.CreateAndBindViewForModelIfNecessary(Shortcut), "RootDialog"));

        public void OnLoaded(object sender, EventArgs e)
        {
            Execute.PostToUIThread(async () =>
            {
                var settings = Settings.Read();
                if (!settings.TryGetValue("DisablePopup", out var disablePopupToken) || !disablePopupToken.Value<bool>())
                {
                    var result = await DialogHost.Show(new InformationMessageDialog(showCheckbox: true)).ConfigureAwait(true);
                    if (result is not bool disablePopup || !disablePopup)
                        return;

                    settings["DisablePopup"] = true;
                    Settings.Write(settings);
                }
            });
        }

        public void OnClosing(object sender, EventArgs e)
        {
            var settings = Settings.Read();
            _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Saving));
            Settings.Write(settings);
        }

        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            window.DragMove();
        }
    }
}
