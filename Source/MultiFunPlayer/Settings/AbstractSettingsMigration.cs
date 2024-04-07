using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal abstract class AbstractSettingsMigration : JsonEditor, ISettingsMigration
{
    public int TargetVersion { get; }
    protected override abstract Logger Logger { get; }

    protected AbstractSettingsMigration() => TargetVersion = int.Parse(GetType().Name[^4..]);
    protected abstract void InternalMigrate(JObject settings);

    public void Migrate(JObject settings)
    {
        InternalMigrate(settings);
        SetPropertyByName(settings, "ConfigVersion", TargetVersion, addIfMissing: true);
    }
}