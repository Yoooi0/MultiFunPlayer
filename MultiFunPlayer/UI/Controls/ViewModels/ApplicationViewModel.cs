using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class ApplicationViewModel : Screen, IHandle<AppSettingsMessage>, IHandle<AppMainWindowCreatedMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public ObservableConcurrentCollection<LogLevel> LogLevels { get; }
    public ObservableConcurrentCollection<string> DeviceTypes { get; }

    public LogLevel SelectedLogLevel { get; set; } = LogLevel.Info;
    public string SelectedDevice { get; set; } = null;
    public bool AlwaysOnTop { get; set; } = false;
    public bool ShowErrorDialogs { get; set; } = true;

    public ApplicationViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);

        LogLevels = new ObservableConcurrentCollection<LogLevel>(LogLevel.AllLevels);

        var devices = SettingsHelper.Read(SettingsType.Devices).Properties().Select(p => p.Name);
        DeviceTypes = new ObservableConcurrentCollection<string>(devices);
    }

    public void OnAlwaysOnTopChanged()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
            return;

        window.Topmost = AlwaysOnTop;
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
        if (message.Action == SettingsAction.Saving)
        {
            message.Settings[nameof(SelectedDevice)] = SelectedDevice;
            message.Settings[nameof(AlwaysOnTop)] = AlwaysOnTop;
            message.Settings[nameof(ShowErrorDialogs)] = ShowErrorDialogs;
            message.Settings["LogLevel"] = JToken.FromObject(SelectedLogLevel ?? LogLevel.Info);
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                SelectedDevice = selectedDevice;
            if (message.Settings.TryGetValue<bool>(nameof(AlwaysOnTop), out var alwaysOnTop))
                AlwaysOnTop = alwaysOnTop;
            if (message.Settings.TryGetValue<bool>(nameof(ShowErrorDialogs), out var showErrorDialogs))
                ShowErrorDialogs = showErrorDialogs;
            if (message.Settings.TryGetValue<LogLevel>("LogLevel", out var logLevel))
                SelectedLogLevel = logLevel;
        }
    }

    public void Handle(AppMainWindowCreatedMessage message) => OnAlwaysOnTopChanged();
}
