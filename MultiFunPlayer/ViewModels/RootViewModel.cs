using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Converters;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stylet;
using StyletIoC;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace MultiFunPlayer.ViewModels
{
    public class RootViewModel : Conductor<IScreen>.Collection.AllActive
    {
        private readonly IEventAggregator _eventAggregator;

        [Inject] public ScriptViewModel Script { get; set; }
        [Inject] public VideoSourceViewModel VideoSource { get; set; }
        [Inject] public OutputTargetViewModel OutputTarget { get; set; }

        public RootViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;

            JsonConvert.DefaultSettings = () =>
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                };

                settings.Converters.Add(new FileSystemInfoConverter());
                settings.Converters.Add(new StringEnumConverter());
                return settings;
            };

            var settings = ReadSettings();
            if (!settings.ContainsKey("LogLevel"))
            {
                settings["LogLevel"] = JToken.FromObject(LogLevel.Info);
                WriteSettings(settings);
            }

            if (settings.TryGetValue("LogLevel", out var logLevelToken))
            {
                var logLevel = LogLevel.FromString(logLevelToken.ToObject<string>());
                var config = new LoggingConfiguration();
                const string layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:|${exception:format=ToString}}";

                config.AddRule(logLevel, LogLevel.Fatal, new FileTarget("file")
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

        protected override void OnActivate()
        {
            Items.Add(Script);
            Items.Add(VideoSource);
            Items.Add(OutputTarget);

            ActivateAndSetParent(Items);
            base.OnActivate();
        }

        public void OnInformationClick()
            => _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new InformationMessageDialog(showCheckbox: false)));

        public void OnLoaded(object sender, EventArgs e)
        {
            Execute.PostToUIThread(async () =>
            {
                var settings = ReadSettings();
                _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Loading));

                if (!settings.TryGetValue("DisablePopup", out var disablePopupToken) || !disablePopupToken.Value<bool>())
                {
                    var result = await DialogHost.Show(new InformationMessageDialog(showCheckbox: true)).ConfigureAwait(true);
                    if (result is not bool disablePopup || !disablePopup)
                        return;

                    settings["DisablePopup"] = true;
                    WriteSettings(settings);
                }
            });
        }

        public void OnClosing(object sender, EventArgs e)
        {
            var settings = ReadSettings();
            _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Saving));
            WriteSettings(settings);
        }

        private JObject ReadSettings()
        {
            var path = Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "MultiFunPlayer.config.json");
            if (!File.Exists(path))
                return new JObject();

            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        private void WriteSettings(JObject settings)
        {
            var path = Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "MultiFunPlayer.config.json");
            File.WriteAllText(path, settings.ToString());
        }

        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            window.DragMove();
        }
    }
}
