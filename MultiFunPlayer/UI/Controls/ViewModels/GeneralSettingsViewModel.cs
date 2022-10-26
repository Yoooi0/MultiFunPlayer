using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class GeneralSettingsViewModel : Screen, IHandle<SettingsMessage>, IHandle<WindowCreatedMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IStyletLoggerManager _styletLoggerManager;

    public ObservableConcurrentCollection<LogLevel> LogLevels { get; }

    public LogLevel SelectedLogLevel { get; set; } = LogLevel.Info;
    public bool EnableUILogging { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;
    public bool ShowErrorDialogs { get; set; } = true;

    public GeneralSettingsViewModel(IStyletLoggerManager styletLoggerManager, IEventAggregator eventAggregator)
    {
        DisplayName = "General";
        eventAggregator.Subscribe(this);

        _styletLoggerManager = styletLoggerManager;
        LogLevels = new ObservableConcurrentCollection<LogLevel>(LogLevel.AllLevels);
    }

    public void OnAlwaysOnTopChanged()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
            return;

        window.Topmost = AlwaysOnTop;
    }

    public void OnEnableUILoggingChanged()
    {
        if (EnableUILogging)
            _styletLoggerManager.ResumeLogging();
        else
            _styletLoggerManager.SuspendLogging();
    }

    public void OnSelectedLogLevelChanged()
    {
        if (SelectedLogLevel == null)
            return;

        Logger.Info("Changing log level to \"{0}\"", SelectedLogLevel.Name);

        var rule = LogManager.Configuration.LoggingRules.FirstOrDefault(r => r.Targets.Any(t => string.Equals(t.Name, "file", StringComparison.OrdinalIgnoreCase)));
        rule?.SetLoggingLevels(SelectedLogLevel, LogLevel.Fatal);
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            message.Settings[nameof(AlwaysOnTop)] = AlwaysOnTop;
            message.Settings[nameof(ShowErrorDialogs)] = ShowErrorDialogs;
            message.Settings["LogLevel"] = JToken.FromObject(SelectedLogLevel ?? LogLevel.Info);
            message.Settings[nameof(EnableUILogging)] = EnableUILogging;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (message.Settings.TryGetValue<bool>(nameof(AlwaysOnTop), out var alwaysOnTop))
                AlwaysOnTop = alwaysOnTop;
            if (message.Settings.TryGetValue<bool>(nameof(ShowErrorDialogs), out var showErrorDialogs))
                ShowErrorDialogs = showErrorDialogs;
            if (message.Settings.TryGetValue<LogLevel>("LogLevel", out var logLevel))
                SelectedLogLevel = logLevel;
            if (message.Settings.TryGetValue<bool>(nameof(EnableUILogging), out var enableUILogging))
                EnableUILogging = enableUILogging;
        }
    }

    public void Handle(WindowCreatedMessage message) => OnAlwaysOnTopChanged();
}
