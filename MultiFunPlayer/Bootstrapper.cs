using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.XInput;
using MultiFunPlayer.MotionProvider;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.Settings;
using MultiFunPlayer.Settings.Converters;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using MultiFunPlayer.VideoSource;
using MultiFunPlayer.VideoSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
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
        builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
        builder.Bind<IMediaResourceFactory>().To<MediaResourceFactory>().InSingletonScope();

        builder.Bind<IVideoSource>().ToAllImplementations().InSingletonScope();
        builder.Bind<IOutputTarget>().ToAllImplementations().InSingletonScope();
        builder.Bind<IMotionProvider>().ToAllImplementations();
        builder.Bind<IShortcutManager>().To<ShortcutManager>().InSingletonScope();
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

        SetupDevice();
        SetupJson();
        SetupLoging();

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

        if (!vcInstalled)
        {
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
    }

    protected override void OnLaunch()
    {
        base.OnLaunch();

        var source = PresentationSource.FromVisual(GetActiveWindow()) as HwndSource;
        var rawInput = Container.GetAll<IInputProcessor>().OfType<RawInputProcessor>().FirstOrDefault();
        rawInput?.RegisterWindow(source);
    }

    private void SetupDevice()
    {
        var settings = SettingsHelper.ReadOrEmpty(SettingsType.Application);
        var devices = SettingsHelper.Read(SettingsType.Devices);

        var serializer = JsonSerializer.Create(new JsonSerializerSettings()
        {
            ContractResolver = new DefaultContractResolver()
        });

        if (!settings.TryGetValue<string>("SelectedDevice", serializer, out var selectedDevice) || selectedDevice == null)
        {
            selectedDevice = devices.Properties().First().Name;
            settings["SelectedDevice"] = selectedDevice;
            SettingsHelper.Write(SettingsType.Application, settings);
        }

        DeviceAxis.LoadSettings(devices[selectedDevice] as JObject, serializer);
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

    private static void SetupLoging()
    {
        var settings = SettingsHelper.ReadOrEmpty(SettingsType.Application);
        if (!settings.ContainsKey("LogLevel"))
            settings["LogLevel"] = JToken.FromObject(LogLevel.Info);

        if (!settings.ContainsKey("LogBlacklist"))
        {
            settings["LogBlacklist"] = JObject.FromObject(new Dictionary<string, LogLevel>()
            {
                [$"{typeof(RawInputProcessor).Namespace}.*"] = LogLevel.Trace,
                [$"{typeof(XInputProcessor).Namespace}.*"] = LogLevel.Trace,
                [$"{typeof(ShortcutViewModel).FullName}"] = LogLevel.Trace
            });
        }

        SettingsHelper.Write(SettingsType.Application, settings);

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
    }
}
