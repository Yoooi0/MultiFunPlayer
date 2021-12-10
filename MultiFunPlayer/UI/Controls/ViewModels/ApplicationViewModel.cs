using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Settings;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class ApplicationViewModel : Screen, IHandle<AppSettingsMessage>
{
    public ObservableConcurrentCollection<string> DeviceTypes { get; }
    public string SelectedDevice { get; set; }
    public bool AlwaysOnTop { get; set; }

    public ApplicationViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);

        var devices = SettingsHelper.Read(SettingsType.Devices).Properties().Select(p => p.Name);
        DeviceTypes = new ObservableConcurrentCollection<string>(devices);
    }

    public void OnAlwaysOnTopChanged()
    {
        Application.Current.MainWindow.Topmost = AlwaysOnTop;
    }

    public void Handle(AppSettingsMessage message)
    {
        if (message.Type == AppSettingsMessageType.Saving)
        {
            message.Settings[nameof(SelectedDevice)] = SelectedDevice;
            message.Settings[nameof(AlwaysOnTop)] = AlwaysOnTop;
        }
        else if (message.Type == AppSettingsMessageType.Loading)
        {
            if (message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                SelectedDevice = selectedDevice;

            if (message.Settings.TryGetValue<bool>(nameof(AlwaysOnTop), out var alwaysOnTop))
                AlwaysOnTop = alwaysOnTop;
        }
    }
}
