using Accessibility;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Converters;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Input.RawInput;
using MultiFunPlayer.Common.Input.XInput;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.VideoSource;
using MultiFunPlayer.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace MultiFunPlayer
{
    public class Bootstrapper : Bootstrapper<RootViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
            builder.Bind<IVideoSource>().ToAllImplementations();
            builder.Bind<IOutputTarget>().ToAllImplementations();

            builder.Bind<ShortcutViewModel>().And<IShortcutManager>().To<ShortcutViewModel>().InSingletonScope();
            builder.Bind<IInputProcessor>().ToAllImplementations().InSingletonScope();
        }

        protected override void OnStart()
        {
            SetupDevice();
            SetupJson();
            SetupLoging();

            var logger = LogManager.GetLogger(nameof(AppDomain));
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                logger.Fatal(e.ExceptionObject as Exception);
                LogManager.Flush();
                if(e.IsTerminating)
                    LogManager.Shutdown();
            };
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
            var settings = Settings.Read();
            var devices = JObject.Parse(File.ReadAllText("MultiFunPlayer.device.json"));

            var serializer = JsonSerializer.Create(new JsonSerializerSettings()
            {
                ContractResolver = new DefaultContractResolver()
            });

            if (!settings.TryGetValue<string>("SelectedDevice", serializer, out var selectedDevice))
            {
                selectedDevice = devices.Properties().Last().Name;
                settings["SelectedDevice"] = selectedDevice;
                Settings.Write(settings);
            }

            var a = devices[selectedDevice].ToObject<List<DeviceAxis>>(serializer);
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
                settings.Converters.Add(new DeviceAxisConverter());
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
            var settings = Settings.Read();
            var settingsDirty = false;
            if (!settings.ContainsKey("LogLevel"))
            {
                settingsDirty = true;
                settings["LogLevel"] = JToken.FromObject(LogLevel.Info);
            }

            if (!settings.ContainsKey("LogBlacklist"))
            {
                settingsDirty = true;
                settings["LogBlacklist"] = JObject.FromObject(new Dictionary<string, LogLevel>() {
                    [$"{typeof(RawInputProcessor).Namespace}.*"] = LogLevel.Trace,
                    [$"{typeof(XInputProcessor).Namespace}.*"] = LogLevel.Trace,
                    [$"{typeof(ShortcutViewModel).FullName}"] = LogLevel.Trace
                });
            }

            if (settingsDirty)
                Settings.Write(settings);

            var config = new LoggingConfiguration();
            const string layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:|${exception:format=ToString}}";

            if(settings.TryGetValue<Dictionary<string, LogLevel>>("LogBlacklist", out var blacklist))
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
}
