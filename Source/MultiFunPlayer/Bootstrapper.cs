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
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MultiFunPlayer;

internal sealed class Bootstrapper : Bootstrapper<RootViewModel>
{
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
        builder.Bind<JsonConverter>().ToAllImplementations();

        builder.Bind<OutputTargetViewModel>().ToSelf().InSingletonScope();
        builder.Bind<SettingsViewModel>().ToSelf().InSingletonScope();
        builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();

        builder.Bind<IMediaSource>().ToAllImplementations().InSingletonScope();
        builder.Bind<IConfigMigration>().ToAllImplementations().InSingletonScope();

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

        ConfigureJson();

        SettingsHelper.Initialize(Container.GetAll<IConfigMigration>());
        var settings = SettingsHelper.ReadOrEmpty();
        var dirty = ConfigureLoging(settings);

        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        var shortcutManager = Container.Get<IShortcutManager>();
        shortcutManager.RegisterAction<LogLevel, string>("Debug::Log",
            s => s.WithLabel("Log level").WithDefaultValue(LogLevel.Info).WithItemsSource(LogLevel.AllLoggingLevels),
            s => s.WithLabel("Message"),
            logger.Log);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            logger.Fatal(e.ExceptionObject as Exception);
            LogManager.Flush();
            if (e.IsTerminating)
                LogManager.Shutdown();
        };

        dirty |= SettingsHelper.Migrate(settings);
        dirty |= ConfigureDevice(settings);

        if (dirty)
            SettingsHelper.Write(settings);

        logger.Info("Environment [OSVersion: {0}, CLRVersion: {1}]", Environment.OSVersion, Environment.Version);
        logger.Info("Assembly [Version: {0}+{1}]", GitVersionInformation.SemVer, GitVersionInformation.FullBuildMetaData);
        logger.Info("Config [Version: {0}]", settings.TryGetValue<int>("ConfigVersion", out var version) ? version : -1);
        logger.Info("Timer [IsHighResolution: {0}, Frequency: {1}]", Stopwatch.IsHighResolution, Stopwatch.Frequency);
        logger.Info("Set working directory to \"{0}\"", workingDirectory);
    }

    protected override void OnStart()
    {
        base.OnStart();

        var vcInstalled = Registry.ClassesRoot?.OpenSubKey("Installer")?.OpenSubKey("Dependencies")
                                              ?.GetSubKeyNames()
                                              ?.Where(s => Regex.IsMatch(s, @"VC,redist\.x64,amd64,14\.\d+,bundle"))
                                              .Any() ?? false;
        if (vcInstalled)
            return;

        var vcDllPresent = Directory.EnumerateFiles(Path.GetDirectoryName(Environment.ProcessPath), "*.dll", SearchOption.AllDirectories)
                                    .Select(f => Path.GetFileName(f))
                                    .Any(f => f.StartsWith("vcruntime140", StringComparison.OrdinalIgnoreCase));
        if (vcDllPresent)
            return;

        var result = MessageBox.Show("To run this application, you must install Visual C++ 2019 x64 redistributable.\nWould you like to download it now?",
                                     $"{nameof(MultiFunPlayer)}.exe",
                                     MessageBoxButton.YesNo, MessageBoxImage.Error);

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

    protected override void Launch()
    {
        PluginCompiler.Initialize(Container);

        _ = Container.Get<RawInputProcessor>();
        _ = Container.Get<XInputProcessor>();

        //TODO: temporary fix due to SettingsViewModel IoC binding causing output targets
        //      to be initialized after shortcuts and clearing all output target actions
        _ = Container.Get<OutputTargetViewModel>();
        _ = RootViewModel;

        var settings = SettingsHelper.ReadOrEmpty();
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new SettingsMessage(settings, SettingsAction.Loading));

        DialogHelper.Initialize(Container.Get<IViewManager>(), Container.Get<SettingsViewModel>());

        base.Launch();
    }

    protected override void OnLaunch()
    {
        var window = GetActiveWindow();
        window.Closing += OnWindowClosing;

        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new WindowCreatedMessage());

        base.OnLaunch();
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        var settings = SettingsHelper.ReadOrEmpty();
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new SettingsMessage(settings, SettingsAction.Saving));
        SettingsHelper.Write(settings);
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
            foreach (var converter in converterFactory())
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

    private bool ConfigureDevice(JObject settings)
    {
        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        var serializer = JsonSerializer.Create(new JsonSerializerSettings()
        {
            ContractResolver = new DefaultContractResolver()
        });

        var dirty = false;
        var defaultDevices = JArray.FromObject(DeviceSettings.DefaultDevices);
        if (!settings.TryGetValue("Devices", out var devicesToken) || devicesToken is not JArray devices)
        {
            settings["Devices"] = defaultDevices;
            devices = defaultDevices;
        }

        var loadedDefaultDevices = devices.Children<JObject>().Where(t => t[nameof(DeviceSettings.IsDefault)].ToObject<bool>()).ToList();
        if (!JToken.DeepEquals(new JArray(loadedDefaultDevices), defaultDevices))
        {
            logger.Info("Updating changes in default devices");
            dirty = true;

            foreach (var deviceToken in loadedDefaultDevices)
                devices.Remove(deviceToken);

            var insertIndex = 0;
            foreach (var deviceToken in defaultDevices)
                devices.Insert(insertIndex++, deviceToken);
        }

        if (!settings.TryGetValue<string>(nameof(DeviceSettingsViewModel.SelectedDevice), serializer, out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice))
        {
            selectedDevice = devices.Last[nameof(DeviceSettings.Name)].ToString();
            settings[nameof(DeviceSettingsViewModel.SelectedDevice)] = selectedDevice;
            dirty = true;
        }

        var device = devices.FirstOrDefault(d => string.Equals(d[nameof(DeviceSettings.Name)].ToString(), selectedDevice, StringComparison.OrdinalIgnoreCase)) as JObject;
        if (device == null)
        {
            logger.Warn("Unable to find device! [SelectedDevice: \"{0}\"]", selectedDevice);
            device = devices.Last as JObject;
            settings[nameof(DeviceSettingsViewModel.SelectedDevice)] = device[nameof(DeviceSettings.Name)].ToString();
            dirty = true;
        }

        DeviceAxis.InitializeFromDevice(device.ToObject<DeviceSettings>());
        return dirty;
    }

    private bool ConfigureLoging(JObject settings)
    {
        var dirty = false;
        if (!settings.ContainsKey("LogLevel"))
        {
            settings["LogLevel"] = JToken.FromObject(LogLevel.Info);
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

        var config = new LoggingConfiguration();
        if (settings.TryGetValue<Dictionary<string, LogLevel>>("LogBlacklist", out var blacklist))
        {
            var blackhole = new NullTarget();
            foreach (var (filter, maxLevel) in blacklist)
                config.AddRule(LogLevel.Trace, maxLevel, blackhole, filter, true);
        }

        if (settings.TryGetValue<LogLevel>("LogLevel", out var minLevel))
        {
            config.AddRule(minLevel, LogLevel.Fatal, new FileTarget("file")
            {
                FileName = @"${basedir}\Logs\latest.log",
                ArchiveFileName = @"${basedir}\Logs\log.{#}.log",
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                ArchiveAboveSize = 5 * 1024 * 1024,
                ArchiveDateFormat = "yyyyMMdd",
                ArchiveOldFileOnStartup = true,
                MaxArchiveFiles = 10,
                OpenFileCacheTimeout = 30,
                AutoFlush = false,
                OpenFileFlushTimeout = 5
            });
        }

        if (Debugger.IsAttached)
        {
            var debugMinLevel = minLevel != null ? LogLevel.FromOrdinal(Math.Min(minLevel.Ordinal, 1)) : LogLevel.Debug;
            config.AddRule(debugMinLevel, LogLevel.Fatal, new DebugSystemTarget("debug"));
        }

        LogManager.Configuration = config;

        var styletLoggerManager = Container.Get<IStyletLoggerManager>();
        Stylet.Logging.LogManager.LoggerFactory = styletLoggerManager.GetLogger;
        Stylet.Logging.LogManager.Enabled = true;

        return dirty;
    }
}
