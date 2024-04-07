using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings;

internal abstract class AbstractSettingsMigration : JsonEditor, ISettingsMigration
{
    public int TargetVersion { get; }
    protected abstract Logger Logger { get; }

    protected AbstractSettingsMigration() => TargetVersion = int.Parse(GetType().Name[^4..]);
    protected abstract void InternalMigrate(JObject settings);

    protected override void Log(LogLevel level, string message, params object[] args)
        => Logger.Log(level, message, args);

    public void Migrate(JObject settings)
    {
        InternalMigrate(settings);
        SetPropertyByName(settings, "ConfigVersion", TargetVersion, addIfMissing: true);
    }
}