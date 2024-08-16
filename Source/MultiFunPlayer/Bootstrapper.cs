using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.TCode;
using MultiFunPlayer.Input.XInput;
using MultiFunPlayer.MediaSource;
using MultiFunPlayer.MotionProvider;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.Plugin;
using MultiFunPlayer.Property;
using MultiFunPlayer.Script.Repository;
using MultiFunPlayer.Script.Repository.ViewModels;
using MultiFunPlayer.Settings;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MultiFunPlayer;

internal sealed class Bootstrapper : Bootstrapper<RootViewModel>
{
    private const string SettingsPath = $"{nameof(MultiFunPlayer)}.config.json";
    private Logger Logger { get; } = LogManager.GetLogger(nameof(MultiFunPlayer));

    static Bootstrapper()
    {
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(int.MaxValue));
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(0));
        ToolTipService.PlacementProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(PlacementMode.Top));

        UIElement.FocusableProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(false));
        Control.IsTabStopProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.ControlTabNavigationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(KeyboardNavigationMode.None));
        KeyboardNavigation.DirectionalNavigationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(KeyboardNavigationMode.None));
    }

    protected override void ConfigureIoC(IStyletIoCBuilder builder)
    {
        builder.Bind<ISnackbarMessageQueue>().To<SnackbarMessageQueue>().InSingletonScope();

        builder.Bind<JsonConverter>().ToAllImplementations();

        builder.Bind<OutputTargetViewModel>().ToSelf().InSingletonScope();
        builder.Bind<SettingsViewModel>().ToSelf().InSingletonScope();
        builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();

        builder.Bind<IMediaSource>().ToAllImplementations().InSingletonScope();
        builder.Bind<ISettingsMigration>().ToAllImplementations().InSingletonScope();
        builder.Bind<MigrationSettingsPreprocessor>().ToSelf().InSingletonScope();
        builder.Bind<DeviceSettingsPreprocessor>().ToSelf().InSingletonScope();

        foreach (var type in ReflectionUtils.FindImplementations<IInputProcessorSettings>())
            builder.Bind(type).And<IInputProcessorSettings>().To(type).InSingletonScope();

        builder.Bind<IInputProcessorFactory>().To<InputProcessorFactory>().InSingletonScope();
        builder.Bind<XInputProcessor>().ToSelf().InSingletonScope();
        builder.Bind<RawInputProcessor>().ToSelf().InSingletonScope();
        builder.Bind<TCodeInputProcessor>().ToSelf();

        builder.Bind<IStyletLoggerManager>().To<StyletLoggerManager>().InSingletonScope();
        builder.Bind<IOutputTargetFactory>().To<OutputTargetFactory>().InSingletonScope();
        builder.Bind<IShortcutManager>().And<IShortcutActionResolver>().To<ShortcutManager>().InSingletonScope();
        builder.Bind<IShortcutActionRunner>().To<ShortcutActionRunner>().InSingletonScope();
        builder.Bind<IShortcutFactory>().To<ShortcutFactory>().InSingletonScope();
        builder.Bind<IPropertyManager>().To<PropertyManager>().InSingletonScope();
        builder.Bind<IMotionProviderFactory>().To<MotionProviderFactory>().InSingletonScope();
        builder.Bind<IMotionProviderManager>().To<MotionProviderManager>().InSingletonScope();

        foreach (var type in ReflectionUtils.FindImplementations<IScriptRepository>())
        {
            var binding = builder.Bind(type).And<IScriptRepository>();
            if (type == typeof(LocalScriptRepository))
                binding = binding.And<ILocalScriptRepository>();

            binding.To(type).InSingletonScope();
        }

        builder.Bind<IScriptRepositoryManager>().To<ScriptRepositoryManager>().InSingletonScope();
    }

    protected override void Configure()
    {
        var workingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        Directory.SetCurrentDirectory(workingDirectory);

        ConfigureLogging();

        Logger.Debug("Bootstrapper Configure");
        ConfigureJson();

        var settings = SettingsHelper.ReadOrEmpty(SettingsPath);
        var dirty = ConfigureLogging(settings);

        var shortcutManager = Container.Get<IShortcutManager>();
        shortcutManager.RegisterAction<LogLevel, string>("Debug::Log",
            s => s.WithLabel("Log level").WithDefaultValue(LogLevel.Info).WithItemsSource(LogLevel.AllLoggingLevels),
            s => s.WithLabel("Message"),
            Logger.Log);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Logger.Fatal(e.ExceptionObject as Exception);
            LogManager.Flush();
            if (e.IsTerminating)
                LogManager.Shutdown();
        };

        dirty |= Container.Get<MigrationSettingsPreprocessor>().Preprocess(settings);
        dirty |= Container.Get<DeviceSettingsPreprocessor>().Preprocess(settings);

        if (dirty)
            SettingsHelper.Write(settings, SettingsPath);

        Logger.Info("Environment [OSVersion: {0}, CLRVersion: {1}]", Environment.OSVersion, Environment.Version);
        Logger.Info("Assembly [Version: {0}+{1}]", GitVersionInformation.SemVer, GitVersionInformation.FullBuildMetaData);

        if (Logger.IsTraceEnabled)
            Logger.Trace(() => $"Config [{settings}]");
        else
            Logger.Info("Config [Version: {0}]", settings.TryGetValue<int>("ConfigVersion", out var version) ? version : -1);

        Logger.Info("Timer [IsHighResolution: {0}, Frequency: {1}]", Stopwatch.IsHighResolution, Stopwatch.Frequency);
        Logger.Info("Set working directory to \"{0}\"", workingDirectory);
    }

    protected override void OnStart()
    {
        base.OnStart();

        CheckVCInstalled();
        CheckWritePermissions();

        static void CheckVCInstalled()
        {
            var vcInstalled = Registry.ClassesRoot?.OpenSubKey("Installer")?.OpenSubKey("Dependencies")
                                                  ?.GetSubKeyNames()
                                                  ?.Where(s => Regex.IsMatch(s, @"VC,redist\.x64,amd64,14\.\d+,bundle"))
                                                  .Any() ?? false;
            if (vcInstalled)
                return;

            var vcDllPresent = Directory.EnumerateFiles(Path.GetDirectoryName(Environment.ProcessPath), "*.dll", SearchOption.AllDirectories)
                                        .Select(Path.GetFileName)
                                        .Any(f => f.StartsWith("vcruntime140", StringComparison.OrdinalIgnoreCase));
            if (vcDllPresent)
                return;

            const string message = """
                To run this application, you must install Visual C++ 2019 x64 redistributable.
                Would you like to download it now?
                """;

            var result = MessageBox.Show(message, $"{nameof(MultiFunPlayer)}.exe", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                    UseShellExecute = true
                });
            }

            Environment.Exit(1157 /* ERROR_DLL_NOT_FOUND */);
        }

        static void CheckWritePermissions()
        {
            var randomFileName = Path.GetRandomFileName();
            try
            {
                using var stream = File.Create(Path.Join(Path.GetDirectoryName(Environment.ProcessPath), randomFileName), 1, FileOptions.DeleteOnClose);
            }
            catch (Exception e)
            {
                var message = $"""
                    File write permissions check failed!
                    {e.GetType()}: {e.Message.Replace(randomFileName, "")}

                    Ensure "{Environment.UserName}" account has sufficient permissions, or run {nameof(MultiFunPlayer)} as administrator.
                    """;

                _ = MessageBox.Show(message, $"{nameof(MultiFunPlayer)}.exe", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(4 /* ERROR_ACCESS_DENIED */);
            }
        }
    }

    protected override void Launch()
    {
        Logger.Debug("Bootstrapper Launch");

        PluginCompiler.Initialize(Container);

        _ = Container.Get<RawInputProcessor>();
        _ = Container.Get<XInputProcessor>();

        //TODO: temporary fix due to SettingsViewModel IoC binding causing output targets
        //      to be initialized after shortcuts and clearing all output target actions
        _ = Container.Get<OutputTargetViewModel>();
        _ = RootViewModel;

        var settings = SettingsHelper.ReadOrEmpty(SettingsPath);
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new SettingsMessage(settings, SettingsAction.Loading));

        DialogHelper.Initialize(Container);

        base.Launch();
    }

    protected override void OnLaunch()
    {
        Logger.Debug("Bootstrapper OnLaunch");

        var window = GetActiveWindow();
        window.Closing += OnWindowClosing;

        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new WindowCreatedMessage());

        base.OnLaunch();
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        Logger.Debug("Bootstrapper OnWindowClosing");

        var settings = SettingsHelper.ReadOrEmpty(SettingsPath);
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new SettingsMessage(settings, SettingsAction.Saving));
        SettingsHelper.Write(settings, SettingsPath);
    }

    private void ConfigureJson()
    {
        var logger = LogManager.GetLogger(nameof(JsonConvert));
        var converterFactory = Container.Get<Func<IEnumerable<JsonConverter>>>();
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            settings.Converters.Add(new StringEnumConverter());
            foreach (var converter in converterFactory().Where(t => t.GetType().GetCustomAttribute<GlobalJsonConverterAttribute>() != null))
                settings.Converters.Add(converter);

            settings.Error += (s, e) =>
            {
                if (e.ErrorContext.Error is JsonSerializationException or JsonReaderException)
                {
                    logger.Warn(e.ErrorContext.Error);
                    e.ErrorContext.Handled = true;
                }
            };

            return settings;
        };
    }

    private void ConfigureLogging()
    {
        var config = new LoggingConfiguration();

        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, LogLevel.Fatal, new FileTarget()
        {
            FileName = @"${basedir}\Logs\application.log",
            ArchiveFileName = @"${basedir}\Logs\application.{#}.log",
            ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
            ArchiveAboveSize = 5 * 1024 * 1024,
            ArchiveDateFormat = "yyyyMMdd",
            ArchiveOldFileOnStartup = true,
            MaxArchiveFiles = 10,
            OpenFileCacheTimeout = 30,
            AutoFlush = false,
            OpenFileFlushTimeout = 5
        }) { RuleName = "application" });

        config.LoggingRules.Add(new LoggingRule("MultiFunPlayer.Settings.Migrations.*", LogLevel.Trace, LogLevel.Fatal, new FileTarget()
        {
            FileName = @"${basedir}\Logs\migration.log",
            ArchiveFileName = @"${basedir}\Logs\migration.{#}.log",
            ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
            ArchiveDateFormat = "yyyyMMdd",
            ArchiveOldFileOnStartup = true,
            MaxArchiveFiles = 10,
            OpenFileCacheTimeout = 30,
            AutoFlush = false,
            OpenFileFlushTimeout = 5,
        }) { RuleName = "migration" } );

        if (Debugger.IsAttached)
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, LogLevel.Fatal, new DebugSystemTarget()) { RuleName = "debug" } );

        LogManager.Configuration = config;

        var styletLoggerManager = Container.Get<IStyletLoggerManager>();
        Stylet.Logging.LogManager.LoggerFactory = styletLoggerManager.GetLogger;
        Stylet.Logging.LogManager.Enabled = true;
    }

    private bool ConfigureLogging(JObject settings)
    {
        var dirty = false;
        if (!settings.TryGetValue<LogLevel>("LogLevel", out var logLevel))
        {
            logLevel = LogLevel.Info;
            settings["LogLevel"] = JToken.FromObject(logLevel);
            dirty = true;
        }

        if (!settings.ContainsKey("LogBlacklist"))
        {
            settings["LogBlacklist"] = JObject.FromObject(new Dictionary<string, LogLevel>()
            {
                [$"{typeof(RawInputProcessor).Namespace}.*"] = LogLevel.Trace,
                [$"{typeof(XInputProcessor).Namespace}.*"] = LogLevel.Trace
            });
            dirty = true;
        }

        var config = LogManager.Configuration;
        if (settings.TryGetValue<Dictionary<string, LogLevel>>("LogBlacklist", out var blacklist))
        {
            var blackhole = new NullTarget();
            foreach (var (filter, maxLevel) in blacklist)
                config.LoggingRules.Insert(0, new LoggingRule(filter, LogLevel.Trace, maxLevel, blackhole) { Final = true });
        }

        LogManager.Configuration.FindRuleByName("application")?.SetLoggingLevels(logLevel, LogLevel.Fatal);
        if (Debugger.IsAttached)
        {
            var debugLogLevel = LogLevel.FromOrdinal(Math.Min(logLevel.Ordinal, 1));
            LogManager.Configuration.FindRuleByName("debug")?.SetLoggingLevels(debugLogLevel, LogLevel.Fatal);
        }

        LogManager.ReconfigExistingLoggers();
        return dirty;
    }
}
