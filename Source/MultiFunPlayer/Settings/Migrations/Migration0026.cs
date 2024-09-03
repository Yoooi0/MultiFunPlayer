using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0026 : AbstractSettingsMigration
{
    protected override void InternalMigrate(JObject settings)
    {
        RenamePropertyByPath(settings, "$.Script.HeatmapShowStrokeLength", "HeatmapShowRange");
    }
}