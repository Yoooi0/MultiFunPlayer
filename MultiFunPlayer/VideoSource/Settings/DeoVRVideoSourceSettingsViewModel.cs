using Newtonsoft.Json;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.Settings
{
    [JsonObject(MemberSerialization.OptIn)]
    public class DeoVRVideoSourceSettingsViewModel : Screen
    {
        [JsonProperty] public string Address { get; set; } = "localhost";
        [JsonProperty] public int Port { get; set; } = 23554;
    }
}
