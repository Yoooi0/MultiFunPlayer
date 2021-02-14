using Newtonsoft.Json;
using Stylet;

namespace MultiFunPlayer.OutputTarget
{
    [JsonObject(MemberSerialization.OptIn)]
    public class DeviceAxisSettings : PropertyChangedBase
    {
        [JsonProperty] public int Minimum { get; set; }
        [JsonProperty] public int Maximum { get; set; }

        public DeviceAxisSettings()
        {
            Minimum = 0;
            Maximum = 100;
        }
    }
}
