using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Common.Messages;

public enum AppSettingsMessageType
{
    Saving,
    Loading
}

public class AppSettingsMessage
{
    public AppSettingsMessageType Type { get; }
    public JObject Settings { get; }

    public AppSettingsMessage(JObject settings, AppSettingsMessageType type)
    {
        Settings = settings;
        Type = type;
    }
}
