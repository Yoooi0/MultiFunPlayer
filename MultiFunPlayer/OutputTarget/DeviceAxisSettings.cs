using Newtonsoft.Json;
using Stylet;

namespace MultiFunPlayer.OutputTarget
{
    [JsonObject(MemberSerialization.OptIn)]
    public class DeviceAxisSettings : PropertyChangedBase
    {
        [JsonProperty] public float Minimum { get; set; }
        [JsonProperty] public float Maximum { get; set; }

        public DeviceAxisSettings()
        {
            Minimum = 0;
            Maximum = 100;
        }
    }
}
