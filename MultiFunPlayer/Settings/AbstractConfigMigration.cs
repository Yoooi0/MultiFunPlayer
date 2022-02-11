using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

public abstract class AbstractConfigMigration : IConfigMigration
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public abstract int TargetVersion { get; }

    public virtual void Migrate(JObject settings)
    {
        settings["ConfigVersion"] = JToken.FromObject(TargetVersion);
    }
}