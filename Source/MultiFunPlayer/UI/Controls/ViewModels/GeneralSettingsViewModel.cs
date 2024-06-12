using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class GeneralSettingsViewModel : Screen, IHandle<SettingsMessage>, IHandle<WindowCreatedMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IStyletLoggerManager _styletLoggerManager;

    public IReadOnlyCollection<LogLevel> LogLevels { get; }

    public LogLevel SelectedLogLevel { get; set; } = LogLevel.Info;
    public bool EnableUILogging { get; set; } = false;
    public bool AllowWindowResize { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;
    public bool ShowErrorDialogs { get; set; } = true;
    public Orientation AppOrientation { get; set; } = Orientation.Vertical;
    public bool RememberWindowLocation { get; set; } = false;

    public GeneralSettingsViewModel(IStyletLoggerManager styletLoggerManager, IEventAggregator eventAggregator)
    {
        DisplayName = "General";
        eventAggregator.Subscribe(this);

        _styletLoggerManager = styletLoggerManager;
        LogLevels = LogLevel.AllLevels.ToList().AsReadOnly();
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

    public void OnAllowWindowResizeChanged()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
            return;

        if (AllowWindowResize)
        {
            window.ResizeMode = ResizeMode.CanResize;
            window.SizeToContent = SizeToContent.Manual;
        }
        else
        {
            window.ResizeMode = ResizeMode.CanMinimize;
            window.SizeToContent = SizeToContent.Height;
        }
    }

    public void OnAppOrientationChanged()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
            return;

        if (AppOrientation == Orientation.Vertical)
            window.Width = window.MinWidth = window.MaxWidth = 600;
        else if (AppOrientation == Orientation.Horizontal)
            window.Width = window.MinWidth = window.MaxWidth = 1200;
    }

    public void OnSelectedLogLevelChanged()
    {
        if (SelectedLogLevel == null)
            return;

        Logger.Info("Changing log level to \"{0}\"", SelectedLogLevel.Name);

        LogManager.Configuration.FindRuleByName("application")?.SetLoggingLevels(SelectedLogLevel, LogLevel.Fatal);
        if (Debugger.IsAttached)
        {
            var debugLogLevel = LogLevel.FromOrdinal(Math.Min(SelectedLogLevel.Ordinal, 1));
            LogManager.Configuration.FindRuleByName("debug")?.SetLoggingLevels(debugLogLevel, LogLevel.Fatal);
        }

        LogManager.ReconfigExistingLoggers();
    }

    public void Handle(SettingsMessage message)
    {
        var settings = message.Settings;

        if (message.Action == SettingsAction.Saving)
        {
            settings[nameof(AlwaysOnTop)] = AlwaysOnTop;
            settings[nameof(ShowErrorDialogs)] = ShowErrorDialogs;
            settings["LogLevel"] = JToken.FromObject(SelectedLogLevel ?? LogLevel.Info);
            settings[nameof(EnableUILogging)] = EnableUILogging;
            settings[nameof(AllowWindowResize)] = AllowWindowResize;
            settings[nameof(AppOrientation)] = JToken.FromObject(AppOrientation);
            settings[nameof(RememberWindowLocation)] = RememberWindowLocation;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(AlwaysOnTop), out var alwaysOnTop))
                AlwaysOnTop = alwaysOnTop;
            if (settings.TryGetValue<bool>(nameof(ShowErrorDialogs), out var showErrorDialogs))
                ShowErrorDialogs = showErrorDialogs;
            if (settings.TryGetValue<LogLevel>("LogLevel", out var logLevel))
                SelectedLogLevel = logLevel;
            if (settings.TryGetValue<bool>(nameof(EnableUILogging), out var enableUILogging))
                EnableUILogging = enableUILogging;
            if (message.Settings.TryGetValue<bool>(nameof(AllowWindowResize), out var allowWindowResize))
                AllowWindowResize = allowWindowResize;
            if (settings.TryGetValue<Orientation>(nameof(AppOrientation), out var appOrientation))
                AppOrientation = appOrientation;
            if (settings.TryGetValue<bool>(nameof(RememberWindowLocation), out var rememberWindowLocation))
                RememberWindowLocation = rememberWindowLocation;
        }
    }

    public void Handle(WindowCreatedMessage message)
    {
        OnAlwaysOnTopChanged();
        OnAllowWindowResizeChanged();
        OnAppOrientationChanged();
    }
}
