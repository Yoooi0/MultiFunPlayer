using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

internal interface IConfigMigration
{
    int TargetVersion { get; }
    void Migrate(JObject settings);
}