using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.XInput;
using MultiFunPlayer.MediaSource;
using MultiFunPlayer.MediaSource.MediaResource;
using MultiFunPlayer.MotionProvider;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.Settings;
using MultiFunPlayer.Settings.Converters;
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
using System.Windows.Interop;

namespace MultiFunPlayer;

public class Bootstrapper : Bootstrapper<RootViewModel>
{
    static Bootstrapper()
    {
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(int.MaxValue));
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(0));
        ToolTipService.PlacementProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(PlacementMode.Top));
    }

    protected override void ConfigureIoC(IStyletIoCBuilder builder)
    {
        builder.Bind<OutputTargetViewModel>().ToSelf().InSingletonScope();
        builder.Bind<SettingsViewModel>().ToSelf().InSingletonScope();
        builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();

        builder.Bind<IMediaSource>().ToAllImplementations().InSingletonScope();
        builder.Bind<IConfigMigration>().ToAllImplementations().InSingletonScope();
        builder.Bind<IInputProcessor>().ToAllImplementations().InSingletonScope();

        builder.Bind<IStyletLoggerManager>().To<StyletLoggerManager>().InSingletonScope();
        builder.Bind<IMediaResourceFactory>().To<MediaResourceFactory>().InSingletonScope();
        builder.Bind<IOutputTargetFactory>().To<OutputTargetFactory>().InSingletonScope();
        builder.Bind<IShortcutManager>().To<ShortcutManager>().InSingletonScope();
        builder.Bind<IShortcutBinder>().To<ShortcutBinder>().InSingletonScope();
        builder.Bind<IMotionProviderFactory>().To<MotionProviderFactory>().InSingletonScope();
        builder.Bind<IMotionProviderManager>().To<MotionProviderManager>().InSingletonScope();
    }

    protected override void Configure()
    {
        var workingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        Directory.SetCurrentDirectory(workingDirectory);

        ConfigureJson();
        var settings = SettingsHelper.ReadOrEmpty();
        var dirty = ConfigureLoging(settings);

        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            logger.Fatal(e.ExceptionObject as Exception);
            LogManager.Flush();
            if (e.IsTerminating)
                LogManager.Shutdown();
        };

        dirty |= MigrateSettings(settings);
        dirty |= ConfigureDevice(settings);

        if (dirty)
            SettingsHelper.Write(settings);

        logger.Info("Environment [OSVersion: {0}, CLRVersion: {1}]", Environment.OSVersion, Environment.Version);
        logger.Info("Assembly [Version: {0}, FileVersion: {1}, InformationalVersion: {2}]", ReflectionUtils.AssemblyVersion, ReflectionUtils.AssemblyFileVersion, ReflectionUtils.AssemblyInformationalVersion);
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
                FileName = "https://docs.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist",
                UseShellExecute = true
            });
        }

        Environment.Exit(1157 /* ERROR_DLL_NOT_FOUND */);
    }

    protected override void Launch()
    {
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

        var source = PresentationSource.FromVisual(GetActiveWindow()) as HwndSource;
        var rawInput = Container.GetAll<IInputProcessor>().OfType<RawInputProcessor>().FirstOrDefault();
        rawInput?.RegisterWindow(source);

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
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            settings.Converters.Add(new LogLevelConverter());
            settings.Converters.Add(new FileSystemInfoConverter());
            settings.Converters.Add(new StringEnumConverter());
            settings.Converters.Add(new EndPointConverter());
            settings.Converters.Add(new DeviceAxisConverter());
            settings.Converters.Add(new TypedValueConverter());
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

    private bool MigrateSettings(JObject settings)
    {
        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        var dirty = false;

        var settingsVersion = settings.TryGetValue<int>("ConfigVersion", out var version) ? version : -1;
        var pendingMigrations = Container.GetAll<IConfigMigration>()
                                         .Where(m => m.TargetVersion > settingsVersion)
                                         .OrderBy(m => m.TargetVersion);

        foreach (var migration in pendingMigrations)
        {
            logger.Info("Migrating settings to version {0}", migration.TargetVersion);
            migration.Migrate(settings);
            dirty = true;
        }

        return dirty;
    }

    private bool ConfigureDevice(JObject settings)
    {
        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        var serializer = JsonSerializer.Create(new JsonSerializerSettings()
        {
            ContractResolver = new DefaultContractResolver()
        });

        var dirty = false;
        var defaultDevices = JArray.FromObject(DeviceSettingsViewModel.DefaultDevices);
        if (!settings.TryGetValue("Devices", out var devicesToken) || devicesToken is not JArray devices)
        {
            settings["Devices"] = defaultDevices;
            devices = defaultDevices;
        }

        var loadedDefaultDevices = devices.Children<JObject>().Where(t => t["Default"].ToObject<bool>()).ToList();
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

        if (!settings.TryGetValue<string>("SelectedDevice", serializer, out var selectedDevice) || string.IsNullOrWhiteSpace(selectedDevice))
        {
            selectedDevice = devices.Last["Name"].ToString();
            settings["SelectedDevice"] = selectedDevice;
            dirty = true;
        }

        var device = devices.FirstOrDefault(d => string.Equals(d["Name"].ToString(), selectedDevice, StringComparison.OrdinalIgnoreCase));
        if (device == null)
        {
            logger.Warn("Unable to find device! [SelectedDevice: \"{0}\"]", selectedDevice);
            device = devices.Last;
            settings["SelectedDevice"] = device["Name"].ToString();
            dirty = true;
        }

        DeviceAxis.LoadSettings(device as JObject, serializer);
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
                [$"{typeof(XInputProcessor).Namespace}.*"] = LogLevel.Trace,
                [$"{typeof(ShortcutSettingsViewModel).FullName}"] = LogLevel.Trace
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
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, new DebugSystemTarget("debug"));
        LogManager.Configuration = config;

        var styletLoggerManager = Container.Get<IStyletLoggerManager>();
        Stylet.Logging.LogManager.LoggerFactory = name => styletLoggerManager.GetLogger(name);
        Stylet.Logging.LogManager.Enabled = true;

        return dirty;
    }
}
