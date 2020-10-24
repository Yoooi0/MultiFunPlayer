using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Stylet;
using StyletIoC;
using System;
using System.Diagnostics;
using System.IO;

namespace MultiFunPlayer.ViewModels
{
    public class RootViewModel : PropertyChangedBase
    {
        private readonly IEventAggregator _eventAggregator;

        [Inject] public ValuesViewModel Values { get; set; }
        [Inject] public PlayerViewModel Player { get; set; }
        [Inject] public DeviceViewModel Device { get; set; }

        public RootViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;

            JsonConvert.DefaultSettings = () =>
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                };

                settings.Converters.Add(new StringEnumConverter());
                return settings;
            };
        }

        public void OnLoaded(object sender, EventArgs e)
        {
            var path = Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "MultiFunPlayer.config.json");
            if (!File.Exists(path))
                return;

            var settings = JObject.Parse(File.ReadAllText(path));
            _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Loading));
        }

        public void OnClosing(object sender, EventArgs e)
        {
            var settings = new JObject();
            _eventAggregator.Publish(new AppSettingsMessage(settings, AppSettingsMessageType.Saving));

            var path = Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "MultiFunPlayer.config.json");
            File.WriteAllText(path, settings.ToString());
        }
    }
}
