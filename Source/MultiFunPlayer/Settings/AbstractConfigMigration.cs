using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings;

internal abstract class AbstractConfigMigration : IConfigMigration
{
    public int TargetVersion { get; }

    protected AbstractConfigMigration() => TargetVersion = int.Parse(GetType().Name[9..]);
    public virtual void Migrate(JObject settings) => settings["ConfigVersion"] = TargetVersion;
}