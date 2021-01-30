using Newtonsoft.Json;
using Stylet;

namespace MultiFunPlayer.VideoSource.Settings
{
    [JsonObject(MemberSerialization.OptIn)]
    public class DeoVRVideoSourceSettingsViewModel : Screen
    {
        [JsonProperty] public string Address { get; set; } = "localhost";
        [JsonProperty] public int Port { get; set; } = 23554;
    }
}
