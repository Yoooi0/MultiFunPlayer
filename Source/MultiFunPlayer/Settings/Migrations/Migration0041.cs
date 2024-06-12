using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0041 : AbstractSettingsMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override void InternalMigrate(JObject settings)
    {
        RemovePropertyByPath(settings, "$.ShowErrorDialogs");
    }
}
