using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Converters;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.VideoSource;
using MultiFunPlayer.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
using System.Diagnostics;

namespace MultiFunPlayer
{
    public class Bootstrapper : Bootstrapper<RootViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            builder.Bind<ScriptViewModel>().And<IDeviceAxisValueProvider>().To<ScriptViewModel>().InSingletonScope();
            builder.Bind<IVideoSource>().ToAllImplementations();
            builder.Bind<IOutputTarget>().ToAllImplementations();
        }

        protected override void OnStart()
        {
            SetupLoging();
            SetupJson();
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

                settings.Converters.Add(new FileSystemInfoConverter());
                settings.Converters.Add(new StringEnumConverter());
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
            if (!settings.ContainsKey("LogLevel"))
            {
                settings["LogLevel"] = JToken.FromObject(LogLevel.Info);
                Settings.Write(settings);
            }

            if (settings.TryGetValue<string>("LogLevel", out var logLevel))
            {
                var config = new LoggingConfiguration();
                const string layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:|${exception:format=ToString}}";

                config.AddRule(LogLevel.FromString(logLevel), LogLevel.Fatal, new FileTarget("file")
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
}
