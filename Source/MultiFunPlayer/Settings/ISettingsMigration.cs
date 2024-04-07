using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

internal interface ISettingsMigration
{
    int TargetVersion { get; }
    void Migrate(JObject settings);
}