using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Common.Messages;

public enum SettingsAction
{
    Saving,
    Loading
}

public class AppSettingsMessage
{
    public SettingsAction Action { get; }
    public JObject Settings { get; }

    public AppSettingsMessage(JObject settings, SettingsAction action)
    {
        Settings = settings;
        Action = action;
    }
}
