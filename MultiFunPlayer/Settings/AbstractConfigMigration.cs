using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

public abstract class AbstractConfigMigration : IConfigMigration
{
    public abstract int TargetVersion { get; }

    public virtual void Migrate(JObject settings)
    {
        settings["ConfigVersion"] = JToken.FromObject(TargetVersion);
    }
}