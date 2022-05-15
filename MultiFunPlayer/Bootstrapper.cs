using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
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
using System.Windows.Interop;

namespace MultiFunPlayer;

public class Bootstrapper : Bootstrapper<RootViewModel>
{
    protected override void ConfigureIoC(IStyletIoCBuilder builder)
    {
        builder.Bind<IConfigMigration>().ToAllImplementations().InSingletonScope();
        builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
        builder.Bind<IMediaResourceFactory>().To<MediaResourceFactory>().InSingletonScope();

        builder.Bind<IMediaSource>().ToAllImplementations().InSingletonScope();
        builder.Bind<IOutputTargetFactory>().To<OutputTargetFactory>().InSingletonScope();
        builder.Bind<IShortcutManager>().To<ShortcutManager>().InSingletonScope();
        builder.Bind<IMotionProviderFactory>().To<MotionProviderFactory>().InSingletonScope();
        builder.Bind<IMotionProviderManager>().To<MotionProviderManager>().InSingletonScope();
        builder.Bind<IInputProcessor>().ToAllImplementations().InSingletonScope();

        builder.Bind<DialogHelper>().To<DialogHelper>().InSingletonScope();
    }

    protected override void Configure()
    {
        var logger = LogManager.GetLogger(nameof(MultiFunPlayer));
        var workingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        Directory.SetCurrentDirectory(workingDirectory);

        _ = Container.Get<DialogHelper>();

        SetupJson();
        var settings = SettingsHelper.ReadOrEmpty(SettingsType.Application);

        var dirty = SetupDevice(settings);
        dirty |= SetupLoging(settings);
        dirty |= MigrateSettings(settings);

        if (dirty)
            SettingsHelper.Write(SettingsType.Application, settings);

        logger.Debug("Timer settings [IsHighResolution: {0}, Frequency: {1}]", Stopwatch.IsHighResolution, Stopwatch.Frequency);
        logger.Info("Set working directory to \"{0}\"", workingDirectory);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            logger.Fatal(e.ExceptionObject as Exception);
            LogManager.Flush();
            if (e.IsTerminating)
                LogManager.Shutdown();
        };
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

    protected override void OnLaunch()
    {
        base.OnLaunch();
        var window = GetActiveWindow();
        window.Closing += OnWindowClosing; 

        var settings = SettingsHelper.ReadOrEmpty(SettingsType.Application);
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new AppSettingsMessage(settings, SettingsAction.Loading));

        var source = PresentationSource.FromVisual(GetActiveWindow()) as HwndSource;
        var rawInput = Container.GetAll<IInputProcessor>().OfType<RawInputProcessor>().FirstOrDefault();
        rawInput?.RegisterWindow(source);
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        var settings = SettingsHelper.ReadOrEmpty(SettingsType.Application);
        var eventAggregator = Container.Get<IEventAggregator>();
        eventAggregator.Publish(new AppSettingsMessage(settings, SettingsAction.Saving));
        SettingsHelper.Write(SettingsType.Application, settings);
    }

    private void SetupJson()
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
            settings.Converters.Add(new IPEndPointConverter());
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

    private bool SetupDevice(JObject settings)
    {
        var devices = SettingsHelper.Read(SettingsType.Devices);
        var serializer = JsonSerializer.Create(new JsonSerializerSettings()
        {
            ContractResolver = new DefaultContractResolver()
        });

        var dirty = false;
        if (!settings.TryGetValue<string>("SelectedDevice", serializer, out var selectedDevice) || selectedDevice == null)
        {
            selectedDevice = devices.Properties().First().Name;
            settings["SelectedDevice"] = selectedDevice;
            dirty = true;
        }

        DeviceAxis.LoadSettings(devices[selectedDevice] as JObject, serializer);
        return dirty;
    }

    private static bool SetupLoging(JObject settings)
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
                [$"{typeof(ShortcutViewModel).FullName}"] = LogLevel.Trace
            });
            dirty = true;
        }

        var config = new LoggingConfiguration();
        const string layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:|${exception:format=ToString}}";
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
                ArchiveAboveSize = 1048576,
                ArchiveDateFormat = "yyyyMMdd",
                ArchiveOldFileOnStartup = true,
                MaxArchiveFiles = 10,
                ConcurrentWrites = false,
                KeepFileOpen = true,
                OpenFileCacheTimeout = 30,
                AutoFlush = false,
                OpenFileFlushTimeout = 5,
                Layout = layout
            });
        }

        if (Debugger.IsAttached)
        {
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, new OutputDebugStringTarget("debug")
            {
                Layout = layout
            });
        }

        LogManager.Configuration = config;
        return dirty;
    }
}
