using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal abstract class AbstractConfigMigration : JsonEditor, IConfigMigration
{
    public int TargetVersion { get; }
    protected override abstract Logger Logger { get; }

    protected AbstractConfigMigration() => TargetVersion = int.Parse(GetType().Name[^4..]);
    public virtual void Migrate(JObject settings) => SetPropertyByName(settings, "ConfigVersion", TargetVersion, addIfMissing: true);
}