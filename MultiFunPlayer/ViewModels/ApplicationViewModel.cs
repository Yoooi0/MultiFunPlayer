using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class ApplicationViewModel : Screen, IHandle<AppSettingsMessage>
    {
        public BindableCollection<string> DeviceTypes { get; }
        public string SelectedDevice { get; set; }

        public ApplicationViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            var devices = JObject.Parse(File.ReadAllText("MultiFunPlayer.device.json")).Properties().Select(p => p.Name);
            DeviceTypes = new BindableCollection<string>(devices);
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                message.Settings[nameof(SelectedDevice)] = SelectedDevice;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if(message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                    SelectedDevice = selectedDevice;
            }
        }
    }
}
