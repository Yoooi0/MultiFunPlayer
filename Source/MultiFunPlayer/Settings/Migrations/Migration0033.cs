using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0033 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        SetPropertyByPath(settings, "$.MediaSource.MPV.AutoStartEnabled", true);
    }
}
