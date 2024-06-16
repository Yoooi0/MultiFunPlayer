using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0037 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        EditPropertyByPath(settings, "$.Script.AutoSkipToScriptStartOffset", v =>
        {
            var x = v.ToObject<double>();
            return x > 0 ? -x : 0.0;
        });
    }
}
