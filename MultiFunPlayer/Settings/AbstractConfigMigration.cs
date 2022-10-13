using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

public abstract class AbstractConfigMigration : IConfigMigration
{
    public int TargetVersion { get; }

    protected AbstractConfigMigration() => TargetVersion = int.Parse(GetType().Name.Split("__")[1]);
    public virtual void Migrate(JObject settings) => settings["ConfigVersion"] = TargetVersion;
}