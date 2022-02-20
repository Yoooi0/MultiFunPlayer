using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class ApplicationViewModel : Screen, IHandle<AppSettingsMessage>
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public ObservableConcurrentCollection<LogLevel> LogLevels { get; }
    public LogLevel SelectedLogLevel { get; set; }
    public ObservableConcurrentCollection<string> DeviceTypes { get; }
    public string SelectedDevice { get; set; }
    public bool AlwaysOnTop { get; set; }

    public ApplicationViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);

        LogLevels = new ObservableConcurrentCollection<LogLevel>(LogLevel.AllLevels);

        var devices = SettingsHelper.Read(SettingsType.Devices).Properties().Select(p => p.Name);
        DeviceTypes = new ObservableConcurrentCollection<string>(devices);
    }

    public void OnAlwaysOnTopChanged()
    {
        Application.Current.MainWindow.Topmost = AlwaysOnTop;
    }

    public void OnSelectedLogLevelChanged()
    {
        if (SelectedLogLevel == null)
            return;

        Logger.Info("Changing log level to \"{0}\"", SelectedLogLevel.Name);

        var rule = LogManager.Configuration.LoggingRules.FirstOrDefault(r => r.Targets.Any(t => string.Equals(t.Name, "file", StringComparison.OrdinalIgnoreCase)));
        rule?.SetLoggingLevels(SelectedLogLevel, LogLevel.Fatal);
    }

    public void Handle(AppSettingsMessage message)
    {
        if (message.Type == AppSettingsMessageType.Saving)
        {
            message.Settings[nameof(SelectedDevice)] = JToken.FromObject(SelectedDevice);
            message.Settings[nameof(AlwaysOnTop)] = JToken.FromObject(AlwaysOnTop);

            if(SelectedLogLevel != null)
                message.Settings["LogLevel"] = JToken.FromObject(SelectedLogLevel);
        }
        else if (message.Type == AppSettingsMessageType.Loading)
        {
            if (message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                SelectedDevice = selectedDevice;

            if (message.Settings.TryGetValue<bool>(nameof(AlwaysOnTop), out var alwaysOnTop))
                AlwaysOnTop = alwaysOnTop;

            if (message.Settings.TryGetValue<LogLevel>("LogLevel", out var logLevel))
                SelectedLogLevel = logLevel;
        }
    }
}
