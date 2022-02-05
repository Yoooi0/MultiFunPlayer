using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

public abstract class AbstractConfigMigration : IConfigMigration
{
    public abstract Version TargetVersion { get; }

    public bool CanMigrateTo(Version version) => version < TargetVersion;
    protected abstract void OnMigrate(JObject settings);

    public bool Migrate(JObject settings)
    {
        OnMigrate(settings);
        settings["ConfigVersion"] = JToken.FromObject(TargetVersion);
        return true;
    }
}